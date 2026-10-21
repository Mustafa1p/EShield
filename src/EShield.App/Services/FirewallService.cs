using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EShield.Models;
namespace EShield.Services;
public sealed record FilterSpec(string? App, string? Ip, bool Permit, ulong Weight, ushort Protocol = 0, ushort Port = 0, bool Inbound = false);
/// <summary>Atomic, process-lifetime WFP filtering. Does not change Windows Firewall profile defaults.</summary>
public sealed class FirewallService : IDisposable
{
    private IntPtr _engine;
    private string? _fingerprint;
    private readonly ConcurrentDictionary<string, (DateTime Time, string[] Ips)> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _resolving = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rulePrefix;
    public bool IsAvailable => _engine != IntPtr.Zero;
    public bool HasDropTelemetry => IsAvailable && EShieldTelemetry(_engine) != 0;
    public string? LastError
    {
        get; private set;
    }
    public string? LastWarning
    {
        get; private set;
    }
    public event Action? AddressesResolved;
    public FirewallService(bool enable = true, string rulePrefix = "[EShield] Rule ")
    {
        _rulePrefix = rulePrefix;
        if (!enable)
        {
            LastError = "Filtering disabled for isolated tests";
            return;
        }
        try
        {
            Check(EShieldOpen(out _engine));
        }
        catch (Exception e) { LastError = e.Message; }
    }
    public bool Matches(string? path, string? target, NetworkProcess process, NetworkFlow flow) => RuleMatcher.Matches(path, target, process, flow) ||
        (!string.IsNullOrEmpty(target) && (string.IsNullOrEmpty(path) || path.Equals(process.ExecutablePath, StringComparison.OrdinalIgnoreCase)) &&
         _resolved.TryGetValue(target, out var answer) && answer.Ips.Contains(flow.RemoteIp, StringComparer.OrdinalIgnoreCase));
    public static List<FilterSpec> BuildPolicy(ConnectionMode mode, IEnumerable<BlockRule> blocks, IEnumerable<AllowRule> allows, IEnumerable<NetworkProcess> processes, AppSettings settings, Func<string, IEnumerable<string>> resolve)
    {
        var list = new List<FilterSpec>();
        var procs = processes.ToArray();
        if (mode != ConnectionMode.AllowAll)
        {
            list.Add(new(null, null, false, 1));
            list.Add(new(null, null, false, 1, Inbound: true));
            if (settings.AllowDnsTraffic)
            {
                list.Add(new(null, null, true, 10, 17, 53));
                list.Add(new(null, null, true, 10, 17, 53, Inbound: true));
            }
            if (settings.AllowSystemProcesses)
                foreach (var p in procs.Where(p => p.IsTrustedSystem))
                    list.Add(new(p.ExecutablePath, null, true, 10));
            foreach (var r in allows)
                AddTarget(r.ExecutablePath, r.Domain, true, 10);
        }
        foreach (var r in blocks)
            AddTarget(r.ExecutablePath, r.Domain, false, 20);
        return list.Distinct().OrderBy(x => x.App).ThenBy(x => x.Ip).ThenBy(x => x.Permit).ToList();
        void AddTarget(string? path, string? target, bool permit, ulong weight)
        {
            if (string.IsNullOrEmpty(target))
            {
                if (!string.IsNullOrEmpty(path))
                    list.Add(new(path, null, permit, weight));
                return;
            }
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (IPAddress.TryParse(target, out var ip))
                ips.Add(ip.ToString());
            else
            {
                foreach (var p in procs.Where(p => string.IsNullOrEmpty(path) || path.Equals(p.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                    foreach (var f in p.Flows.Where(f => RuleMatcher.Matches(path, target, p, f)))
                        ips.Add(f.RemoteIp);
                foreach (var addr in resolve(target))
                    ips.Add(addr);
            }
            foreach (var addr in ips.Where(a => IPAddress.TryParse(a, out _)))
                list.Add(new(path, addr, permit, weight));
        }
    }
    public bool Configure(ConnectionMode mode, IEnumerable<BlockRule> blocks, IEnumerable<AllowRule> allows, IEnumerable<NetworkProcess> processes, AppSettings settings)
    {
        if (!IsAvailable)
            return false;
        var policy = BuildPolicy(mode, blocks, allows, processes, settings, Resolve);
        var fingerprint = string.Join("\n", policy);
        if (fingerprint == _fingerprint)
            return true;
        try
        {
            Check(EShieldBegin(_engine));
            foreach (var spec in policy)
            {
                if (spec.App != null && !File.Exists(spec.App))
                    continue;
                foreach (var v6 in new[] { false, true })
                {
                    if (spec.Ip != null && IPAddress.Parse(spec.Ip).AddressFamily != (v6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork))
                        continue;
                    Check(EShieldAdd(_engine, spec.App, spec.Ip, spec.Permit ? 1 : 0, spec.Weight, spec.Protocol, spec.Port, spec.Inbound ? 1 : 0, v6 ? 1 : 0));
                    // Apply explicit rules to inbound connections as well; default deny is outbound only.
                    if (!spec.Inbound && (spec.App != null || spec.Ip != null))
                        Check(EShieldAdd(_engine, spec.App, spec.Ip, spec.Permit ? 1 : 0, spec.Weight, spec.Protocol, spec.Port, 1, v6 ? 1 : 0));
                }
            }
            Check(EShieldCommit(_engine));
            _fingerprint = fingerprint;
            LastError = null;
            try
            {
                SyncPersistentBlocks(policy.Where(s => !s.Permit && s.Weight == 20));
                LastWarning = null;
            }
            catch (Exception e) { LastWarning = "Saved firewall rules: " + e.Message; }
            return true;
        }
        catch (Exception e) { EShieldAbort(_engine); LastError = e.Message; return false; }
    }
    public IEnumerable<string> Resolve(string target)
    {
        if (target.StartsWith("*."))
            return Array.Empty<string>();
        if (_resolved.TryGetValue(target, out var v) && DateTime.UtcNow - v.Time < TimeSpan.FromMinutes(1))
            return v.Ips;
        if (_resolving.TryAdd(target, 0))
            _ = Task.Run(async () =>
            {
                try
                {
                    var ips = await Dns.GetHostAddressesAsync(target).WaitAsync(TimeSpan.FromSeconds(5));
                    _resolved[target] = (DateTime.UtcNow, ips.Select(a => a.ToString()).ToArray());
                    AddressesResolved?.Invoke();
                }
                catch { _resolved[target] = (DateTime.UtcNow, Array.Empty<string>()); }
                finally { _resolving.TryRemove(target, out _); }
            });
        return v.Ips ?? Array.Empty<string>();
    }
    public DropEvent[] DrainDrops()
    {
        if (!IsAvailable)
            return Array.Empty<DropEvent>();
        var events = new DropEvent[256];
        int count = EShieldDrain(_engine, events, events.Length);
        return events.Take(count).ToArray();
    }
    // Explicit Block List rules survive quitting. Strict-mode defaults remain
    // dynamic so a crash cannot leave the machine in default-deny mode.
    private void SyncPersistentBlocks(IEnumerable<FilterSpec> specs)
    {
        var prefix = _rulePrefix;
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        dynamic rules = policy.Rules;
        var desired = new Dictionary<string, (FilterSpec Spec, int Direction)>();
        foreach (var spec in specs.Where(s => s.App == null || File.Exists(s.App)))
            foreach (var direction in new[] { 1, 2 })
            {
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{spec.App?.ToLowerInvariant()}|{spec.Ip}|{direction}")))[..24];
                desired[prefix + id] = (spec, direction);
            }
        var existing = new HashSet<string>();
        foreach (dynamic rule in rules)
        {
            string name = rule.Name;
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                existing.Add(name);
            }
        }
        foreach (var name in existing.Except(desired.Keys))
            rules.Remove(name);
        foreach (var pair in desired.Where(p => !existing.Contains(p.Key)))
        {
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            rule.Name = pair.Key;
            rule.Description = "EShield explicit Block List destination rule";
            rule.Grouping = "EShield";
            rule.Protocol = 256;
            rule.Profiles = 7;
            rule.Direction = pair.Value.Direction;
            rule.Action = 0;
            if (pair.Value.Spec.App != null)
                rule.ApplicationName = pair.Value.Spec.App;
            rule.RemoteAddresses = pair.Value.Spec.Ip ?? "*";
            rule.Enabled = true;
            rules.Add(rule);
        }
    }
    public void ApplyAllowAllMode()
    {
        if (!IsAvailable)
            return;
        Check(EShieldBegin(_engine));
        Check(EShieldCommit(_engine));
        _fingerprint = "";
    }
    private static void Check(uint error)
    {
        if (error != 0)
            throw new Win32Exception(unchecked((int)error), $"WFP error 0x{error:X8}. {new Win32Exception(unchecked((int)error)).Message}");
    }
    public void Dispose()
    {
        if (_engine == IntPtr.Zero)
            return;
        EShieldClose(_engine);
        _engine = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DropEvent
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string App;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Ip;
        public ushort Port; public byte Protocol; public byte Inbound;
    }
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern uint EShieldOpen(out IntPtr engine);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern uint EShieldBegin(IntPtr engine);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern uint EShieldAdd(IntPtr engine, string? app, string? ip, int permit, ulong weight, ushort protocol, ushort port, int inbound, int v6);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern uint EShieldCommit(IntPtr engine);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void EShieldAbort(IntPtr engine);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int EShieldDrain(IntPtr engine, [Out] DropEvent[] events, int count);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int EShieldTelemetry(IntPtr engine);
    [DllImport("EShield.Native.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void EShieldClose(IntPtr engine);
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using EShield.Models;
namespace EShield.Services;
public sealed class NetworkMonitorService : IDisposable
{
    public event Action<NetworkProcess, NetworkFlow>? NewConnection;
    public event Action<List<NetworkProcess>>? ProcessesUpdated;
    public event Action<long, long>? BandwidthUpdated;
    public readonly DnsCacheService DnsCache = new();
    public readonly DnsResolverService Dns = new();
    private readonly Dictionary<string, NetworkProcess> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NetworkFlow> _flows = new();
    private readonly Dictionary<int, (string Path, string Name)> _pidCache = new();
    private readonly ConcurrentQueue<Observation> _queue = new();
    private readonly ConcurrentQueue<int> _expiredPids = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private TraceEventSession? _networkSession, _dnsSession;
    private bool _running;
    private int _polling;
    private DateTime _lastSnapshot = DateTime.MinValue;
    private long _sent, _received;
    public bool IsEventMonitoring
    {
        get; private set;
    }
    public bool IsDnsTracing
    {
        get; private set;
    }
    public string Status { get; private set; } = "Starting Windows network monitoring…";
    private record Observation(int Pid, string Ip, int Port, int LocalPort, FlowProtocol Protocol, bool Receive, long Bytes, bool Closed = false, bool Accepted = false, string? Path = null, bool Denied = false);
    public void Start()
    {
        if (_running)
            return;
        _running = true;
        DnsCache.HostnameResolved += (_, _) => { };
        DnsCache.Start();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _ = Task.Run(StartTracing);
    }
    private void StartTracing()
    {
        try
        {
            _networkSession = new TraceEventSession($"EShield-Network-{Environment.ProcessId}") { StopOnDispose = true };
            _networkSession.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);
            var k = _networkSession.Source.Kernel;
            k.ProcessStart += e => _expiredPids.Enqueue(e.ProcessID);
            k.ProcessStop += e => _expiredPids.Enqueue(e.ProcessID);
            k.TcpIpSend += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, e.size));
            k.TcpIpRecv += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.TCP, true, e.size));
            k.TcpIpConnect += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, 0));
            k.TcpIpAccept += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.TCP, true, 0, Accepted: true));
            k.TcpIpDisconnect += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, 0, Closed: true));
            k.TcpIpSendIPV6 += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, e.size));
            k.TcpIpRecvIPV6 += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.TCP, true, e.size));
            k.TcpIpConnectIPV6 += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, 0));
            k.TcpIpAcceptIPV6 += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.TCP, true, 0, Accepted: true));
            k.TcpIpDisconnectIPV6 += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.TCP, false, 0, Closed: true));
            k.UdpIpSend += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.UDP, false, e.size));
            k.UdpIpRecv += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.UDP, true, e.size));
            k.UdpIpSendIPV6 += e => Queue(new(e.ProcessID, e.daddr.ToString(), e.dport, e.sport, FlowProtocol.UDP, false, e.size));
            k.UdpIpRecvIPV6 += e => Queue(new(e.ProcessID, e.saddr.ToString(), e.sport, e.dport, FlowProtocol.UDP, true, e.size));
            IsEventMonitoring = true;
            Status = "Windows ETW · TCP/UDP · IPv4/IPv6";
            _ = Task.Run(StartDnsTracing);
            if (_running)
                _networkSession.Source.Process();
        }
        catch (Exception e) { Status = $"TCP snapshot fallback · ETW unavailable: {e.Message}"; }
    }
    private void StartDnsTracing()
    {
        try
        {
            _dnsSession = new TraceEventSession($"EShield-DNS-{Environment.ProcessId}") { StopOnDispose = true };
            _dnsSession.Source.Dynamic.All += e =>
            {
                try
                {
                    if (!e.PayloadNames.Contains("QueryName") || !e.PayloadNames.Contains("QueryResults"))
                        return;
                    var name = e.PayloadByName("QueryName")?.ToString() ?? "";
                    var results = e.PayloadByName("QueryResults")?.ToString() ?? "";
                    foreach (Match m in Regex.Matches(results, @"(?:\d{1,3}\.){3}\d{1,3}|[0-9a-fA-F]*:[0-9a-fA-F:]+"))
                        DnsCache.RecordAnswer(m.Value, name, e.ProcessID);
                }
                catch { }
            };
            _dnsSession.EnableProvider("Microsoft-Windows-DNS-Client", TraceEventLevel.Verbose, ulong.MaxValue);
            IsDnsTracing = true;
            if (_running)
                _dnsSession.Source.Process();
        }
        catch { }
    }
    private void Queue(Observation o)
    {
        if (_running && o.Port > 0 && o.Pid >= 0 && _queue.Count < 50000)
            _queue.Enqueue(o);
    }
    public void AddBlockedAttempt(FirewallService.DropEvent drop)
    {
        if (drop.Protocol is not (6 or 17))
            return;
        Queue(new(0, drop.Ip, drop.Port, 0, drop.Protocol == 6 ? FlowProtocol.TCP : FlowProtocol.UDP, drop.Inbound != 0, 0, Accepted: drop.Inbound != 0, Path: DevicePathToDos(drop.App), Denied: true));
    }
    private void Tick()
    {
        if (!_running)
            return;
        while (_expiredPids.TryDequeue(out var pid))
            _pidCache.Remove(pid);
        if (DateTime.Now - _lastSnapshot > TimeSpan.FromSeconds(3) && Interlocked.Exchange(ref _polling, 1) == 0)
        {
            _lastSnapshot = DateTime.Now;
            _ = Task.Run(() => { try { Snapshot(false); Snapshot(true); } finally { Volatile.Write(ref _polling, 0); } });
        }
        int count = 0;
        while (count++ < 50000 && _queue.TryDequeue(out var o))
            Record(o);
        foreach (var p in _processes.Values)
        {
            foreach (var f in p.Flows)
            {
                var names = DnsCache.GetHostnames(f.RemoteIp, f.ParentProcessId);
                foreach (var n in names)
                    f.HostAliases.Add(n);
                if (names.Length > 0)
                    f.Hostname = names.OrderBy(x => x.Length).First();
                if (f.IsActive && DateTime.Now - f.LastSeen > TimeSpan.FromMinutes(2))
                    f.IsActive = false;
            }
            RebuildDestinations(p);
            p.RecordBandwidthDelta();
        }
        BandwidthUpdated?.Invoke(_received * 4 / 3, _sent * 4 / 3);
        _sent = 0;
        _received = 0;
        ProcessesUpdated?.Invoke(_processes.Values.ToList());
    }
    private void Record(Observation o)
    {
        var info = o.Path != null ? (Path: o.Path, Name: Path.GetFileNameWithoutExtension(o.Path)) : ProcessInfo(o.Pid);
        if (string.IsNullOrEmpty(info.Name))
            return;
        var key = string.IsNullOrEmpty(info.Path) ? $"pid:{o.Pid}" : info.Path;
        if (!_processes.TryGetValue(key, out var p))
        {
            p = new()
            {
                Pid = o.Pid,
                Name = info.Name,
                ExecutablePath = info.Path
            };
            _processes[key] = p;
            p.AppIcon = IconService.GetIcon(info.Path);
            _ = Task.Run(() => SignatureService.Inspect(info.Path)).ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() => { if (t.IsCompletedSuccessfully) { p.SignatureText = t.Result.Text; p.IsTrustedSystem = t.Result.TrustedSystem; p.Refresh(); } }));
        }
        var flowKey = $"{key}|{o.Pid}|{o.LocalPort}|{o.Ip}|{o.Port}|{o.Protocol}";
        if (!_flows.TryGetValue(flowKey, out var f))
        {
            if (o.Closed)
                return;
            f = new()
            {
                ParentProcessId = o.Pid,
                RemoteIp = o.Ip,
                RemotePort = o.Port,
                LocalPort = o.LocalPort,
                Protocol = o.Protocol,
                Direction = o.Accepted || (o.Protocol == FlowProtocol.UDP && o.Receive) ? EShield.Models.FlowDirection.Inbound : EShield.Models.FlowDirection.Outbound,
                WasDenied = o.Denied
            };
            f.Hostname = DnsCache.GetHostname(o.Ip) ?? Dns.GetOrQueue(o.Ip);
            _flows[flowKey] = f;
            p.Flows.Add(f);
            NewConnection?.Invoke(p, f);
        }
        f.LastSeen = DateTime.Now;
        f.IsActive = !o.Closed && !o.Denied;
        f.WasDenied = o.Denied;
        if (o.Receive)
        {
            f.BytesReceived += o.Bytes;
            p.BytesReceived += o.Bytes;
            _received += o.Bytes;
        }
        else
        {
            f.BytesSent += o.Bytes;
            p.BytesSent += o.Bytes;
            _sent += o.Bytes;
        }
        p.LastSeen = f.LastSeen;
        if (o.Pid > 0)
            p.Pid = o.Pid;
    }
    private (string Path, string Name) ProcessInfo(int pid)
    {
        if (_pidCache.TryGetValue(pid, out var info))
            return info;
        try
        {
            using var p = Process.GetProcessById(pid);
            var path = GetProcessPath(pid);
            return _pidCache[pid] = (path, p.ProcessName);
        }
        catch { return ("", ""); }
    }
    public IReadOnlyList<NetworkProcess> GetProcesses() => _processes.Values.ToArray();
    private static void RebuildDestinations(NetworkProcess p)
    {
        var groups = new Dictionary<string, List<NetworkFlow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in p.Flows)
        {
            var names = f.HostAliases.Count > 0 ? f.HostAliases.ToArray() : !string.IsNullOrEmpty(f.Hostname) ? new[] { f.Hostname } : new[] { "IP Addresses" };
            foreach (var name in names)
            {
                if (!groups.TryGetValue(name!, out var flows))
                    groups[name!] = flows = new();
                flows.Add(f);
            }
        }
        foreach (var stale in p.Destinations.Where(d => !groups.ContainsKey(d.Key)).ToArray())
            p.Destinations.Remove(stale);
        foreach (var pair in groups.OrderBy(x => x.Key == "IP Addresses" ? "" : x.Key))
        {
            var d = p.Destinations.FirstOrDefault(d => d.Key == pair.Key);
            if (d == null)
            {
                d = new()
                {
                    Key = pair.Key,
                    IsIpGroup = pair.Key == "IP Addresses"
                };
                p.Destinations.Add(d);
            }
            d.Flows = pair.Value;
            d.IpAddresses = pair.Value.Select(f => f.RemoteIp).Distinct().ToList();
            d.Refresh();
        }
    }
    public void ClearHistory()
    {
        _processes.Clear();
        _flows.Clear();
        _pidCache.Clear();
        while (_queue.TryDequeue(out _))
        {
        }
    }
    public void Stop()
    {
        _running = false;
        _timer.Stop();
        _networkSession?.Dispose();
        _dnsSession?.Dispose();
        DnsCache.Dispose();
    }
    public void Dispose() => Stop();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint QueryDosDevice(string name, StringBuilder target, int size);
    private static string GetProcessPath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero)
            return "";
        try
        {
            var b = new StringBuilder(32768);
            int n = b.Capacity;
            return QueryFullProcessImageName(h, 0, b, ref n) ? b.ToString() : "";
        }
        finally { CloseHandle(h); }
    }
    private static string DevicePathToDos(string path)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            var b = new StringBuilder(512);
            if (QueryDosDevice(drive.Name[..2], b, b.Capacity) != 0 && path.StartsWith(b.ToString(), StringComparison.OrdinalIgnoreCase))
                return drive.Name[..2] + path[b.Length..];
        }
        return path;
    }
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int family, int tableClass, uint reserved);
    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp4
    {
        public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, Pid;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Local; public uint LocalScope, LocalPort; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Remote; public uint RemoteScope, RemotePort, State, Pid;
    }
    private void Snapshot(bool v6)
    {
        uint size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, v6 ? 23 : 2, 5, 0);
        if (size == 0)
            return;
        var b = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetExtendedTcpTable(b, ref size, false, v6 ? 23 : 2, 5, 0) != 0)
                return;
            int count = Marshal.ReadInt32(b), rowSize = v6 ? Marshal.SizeOf<Tcp6>() : Marshal.SizeOf<Tcp4>();
            for (int i = 0; i < count; i++)
            {
                var row = IntPtr.Add(b, 4 + i * rowSize);
                uint state, port, local, pid;
                string ip;
                if (v6)
                {
                    var r = Marshal.PtrToStructure<Tcp6>(row);
                    state = r.State;
                    port = r.RemotePort;
                    local = r.LocalPort;
                    pid = r.Pid;
                    ip = new IPAddress(r.Remote, r.RemoteScope).ToString();
                }
                else
                {
                    var r = Marshal.PtrToStructure<Tcp4>(row);
                    state = r.State;
                    port = r.RemotePort;
                    local = r.LocalPort;
                    pid = r.Pid;
                    ip = new IPAddress(r.RemoteAddr).ToString();
                }
                if (state is not (3 or 4 or 5))
                    continue;
                Queue(new((int)pid, ip, Port(port), Port(local), FlowProtocol.TCP, false, 0));
            }
        }
        finally { Marshal.FreeHGlobal(b); }
    }
    private static int Port(uint value) => (int)(((value & 255) << 8) | ((value >> 8) & 255));
}

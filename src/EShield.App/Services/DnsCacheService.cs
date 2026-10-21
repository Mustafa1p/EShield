using System.Collections.Concurrent;
using System.Management;
using System.Net;
namespace EShield.Services;
/// <summary>All observed forward DNS names, with per-process ETW answers preferred over PTR names.</summary>
public sealed class DnsCacheService : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _processNames = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _timer;
    private int _polling;
    public event Action<string, string>? HostnameResolved;
    public void Start()
    {
        _timer = new(_ => Refresh(), null, 0, 3000);
    }
    public string? GetHostname(string ip) => GetHostnames(ip).OrderBy(x => x.Length).FirstOrDefault();
    public string[] GetHostnames(string ip, int pid = 0)
    {
        if (pid > 0 && _processNames.TryGetValue($"{pid}|{ip}", out var p))
            return p.Keys.ToArray();
        return _names.TryGetValue(ip, out var n) ? n.Keys.ToArray() : Array.Empty<string>();
    }
    public string Best(string? name, string ip) => string.IsNullOrEmpty(name) ? GetHostname(ip) ?? ip : name;
    public void RecordAnswer(string ip, string name, int pid = 0)
    {
        if (!IPAddress.TryParse(ip, out var parsed) || string.IsNullOrWhiteSpace(name))
            return;
        ip = parsed.ToString();
        name = name.TrimEnd('.').ToLowerInvariant();
        if (pid > 0)
            _processNames.GetOrAdd($"{pid}|{ip}", _ => new(StringComparer.OrdinalIgnoreCase)).TryAdd(name, 0);
        if (_names.GetOrAdd(ip, _ => new(StringComparer.OrdinalIgnoreCase)).TryAdd(name, 0))
            HostnameResolved?.Invoke(ip, name);
    }
    private void Refresh()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0)
            return;
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\standardcimv2"), new ObjectQuery("SELECT Entry, Data, Type FROM MSFT_DNSClientCache"));
            foreach (ManagementObject row in searcher.Get())
                using (row)
                {
                    var type = Convert.ToInt32(row["Type"]);
                    if (type is 1 or 28)
                        RecordAnswer(row["Data"]?.ToString() ?? "", row["Entry"]?.ToString() ?? "");
                }
        }
        catch { }
        finally { Volatile.Write(ref _polling, 0); }
    }
    public void Dispose() => _timer?.Dispose();
}

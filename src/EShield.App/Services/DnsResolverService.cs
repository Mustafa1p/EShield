using System.Collections.Concurrent;
using System.Net;
namespace EShield.Services;
public sealed class DnsResolverService
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly SemaphoreSlim _sem = new(8, 8);
    public event Action<string, string>? HostnameResolved;
    public string? GetOrQueue(string ip)
    {
        if (_cache.TryGetValue(ip, out var h))
            return h;
        if (IPAddress.TryParse(ip, out _) && _pending.TryAdd(ip, 0))
            _ = ResolveAsync(ip);
        return null;
    }
    public string GetBest(string? hostname, string ip) => !string.IsNullOrEmpty(hostname) ? hostname : _cache.GetValueOrDefault(ip, ip);
    private async Task ResolveAsync(string ip)
    {
        await _sem.WaitAsync();
        try
        {
            var e = await Dns.GetHostEntryAsync(ip).WaitAsync(TimeSpan.FromSeconds(3));
            var h = e.HostName.TrimEnd('.');
            _cache[ip] = h;
            if (h != ip)
                HostnameResolved?.Invoke(ip, h);
        }
        catch { _cache[ip] = ip; }
        finally { _sem.Release(); _pending.TryRemove(ip, out _); }
    }
}

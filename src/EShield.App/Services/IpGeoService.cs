using System.Collections.Concurrent;
using System.IO;
using System.Net;
using MaxMind.Db;
namespace EShield.Services;
public record GeoLocation(double Lat, double Lon, string Country, string City, string CountryCode, string Region = "");
/// <summary>Local DB-IP lookup. No monitored addresses are sent to a server.</summary>
public class IpGeoService
{
    private static readonly Lazy<Reader?> Database = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "dbip-city-lite.mmdb");
        return File.Exists(path) ? new Reader(path) : null;
    });
    private readonly ConcurrentDictionary<string, GeoLocation> _cache = new();
    public event Action<string, GeoLocation>? LocationResolved;
    public bool IsAvailable => Database.Value != null;
    public Task<GeoLocation?> LookupAsync(string ip)
    {
        if (_cache.TryGetValue(ip, out var cached))
            return Task.FromResult<GeoLocation?>(cached);
        if (!IPAddress.TryParse(ip, out var address) || IsPrivate(address) || Database.Value == null)
            return Task.FromResult<GeoLocation?>(null);
        try
        {
            var data = Database.Value.Find<Dictionary<string, object>>(address);
            if (data == null)
                return Task.FromResult<GeoLocation?>(null);
            var country = Child(data, "country");
            var city = Child(data, "city");
            var location = Child(data, "location");
            var region = "";
            if (data.TryGetValue("subdivisions", out var sub) && sub is IEnumerable<object> list && list.FirstOrDefault() is Dictionary<string, object> first)
                region = Name(first);
            var loc = new GeoLocation(Number(location, "latitude"), Number(location, "longitude"), Name(country), Name(city), Text(country, "iso_code"), region);
            _cache[ip] = loc;
            LocationResolved?.Invoke(ip, loc);
            return Task.FromResult<GeoLocation?>(loc);
        }
        catch { return Task.FromResult<GeoLocation?>(null); }
    }
    public async Task LookupBatchAsync(IEnumerable<string> ips)
    {
        foreach (var ip in ips.Distinct())
            await LookupAsync(ip);
    }
    public GeoLocation? GetCached(string ip) => _cache.GetValueOrDefault(ip);
    private static Dictionary<string, object> Child(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is Dictionary<string, object> x ? x : new();
    private static string Text(Dictionary<string, object> d, string k) => d.GetValueOrDefault(k)?.ToString() ?? "";
    private static string Name(Dictionary<string, object> d) => Text(Child(d, "names"), "en");
    private static double Number(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? Convert.ToDouble(v) : 0;
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
            return true;
        var b = ip.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xfe) == 0xfc;
        return b[0] is 0 or 10 or 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127) || b[0] >= 224;
    }
}

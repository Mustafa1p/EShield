using System.Globalization;
using System.Net;
using EShield.Models;
namespace EShield.Services;
public static class RuleMatcher
{
    public static string NormalizeTarget(string input)
    {
        var value = input.Trim().TrimEnd('.').ToLowerInvariant();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            value = uri.IdnHost;
        if (IPAddress.TryParse(value.Trim('[', ']'), out var ip))
            return ip.ToString();
        var wildcard = value.StartsWith("*.");
        var name = wildcard ? value[2..] : value;
        if (name.Length == 0 || name.Contains('/') || name.Contains(':') || name.Contains(' ') || name.Contains('*'))
            throw new ArgumentException("Enter a domain, *.example.com, IP address, or executable path.");
        name = new IdnMapping().GetAscii(name);
        if (Uri.CheckHostName(name) == UriHostNameType.Unknown)
            throw new ArgumentException("Invalid domain or IP address.");
        return wildcard ? "*." + name : name;
    }
    public static bool MatchesHost(string target, string? hostname, string ip)
    {
        if (IPAddress.TryParse(target, out var address))
            return IPAddress.TryParse(ip, out var remote) && address.Equals(remote);
        var host = hostname?.TrimEnd('.');
        if (string.IsNullOrEmpty(host))
            return false;
        return target.StartsWith("*.") ? host.EndsWith(target[1..], StringComparison.OrdinalIgnoreCase) && host.Length > target.Length - 1
            : host.Equals(target, StringComparison.OrdinalIgnoreCase);
    }
    public static bool Matches(string? path, string? target, NetworkProcess proc, NetworkFlow flow)
    {
        if (!string.IsNullOrEmpty(path) && !path.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(target))
            return MatchesHost(target, flow.Hostname, flow.RemoteIp) || flow.HostAliases.Any(h => MatchesHost(target, h, flow.RemoteIp));
        return !string.IsNullOrEmpty(path);
    }
}

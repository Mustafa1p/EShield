using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Collections.ObjectModel;
using EShield.Services;
namespace EShield.Models;
public enum ConnectionMode
{
    AllowAll, RequireApproval, BlockAll
}
public enum FlowProtocol
{
    TCP, UDP
}
public enum FlowDirection
{
    Outbound, Inbound
}
public enum RuleType
{
    App, Domain, Both
}
public class NotifyModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(n);
        return true;
    }
}
public class DestinationGroup : NotifyModel
{
    public string Key { get; set; } = "";
    public bool IsIpGroup
    {
        get; set;
    }
    private List<NetworkFlow> _flows = new();
    public List<NetworkFlow> Flows
    {
        get => _flows; set
        {
            var changed = !_flows.SequenceEqual(value);
            _flows = value;
            if (changed)
                OnPropertyChanged();
        }
    }
    private bool _blocked, _hovered, _expanded, _selected;
    public bool IsBlocked
    {
        get => _blocked; set => Set(ref _blocked, value);
    }
    public bool IsHovered
    {
        get => _hovered; set => Set(ref _hovered, value);
    }
    public bool IsExpanded
    {
        get => _expanded; set => Set(ref _expanded, value);
    }
    public bool IsSelected
    {
        get => _selected; set => Set(ref _selected, value);
    }
    public string? CountryCode
    {
        get; set;
    }
    public string? Country
    {
        get; set;
    }
    public string? City
    {
        get; set;
    }
    public string? Region
    {
        get; set;
    }
    public string Flag => CountryCode ?? "";
    public string LocationText => IsIpGroup ? $"{ConnectionCount} connections" : string.Join(" · ", new[] { City, Region, Country }.Where(x => !string.IsNullOrEmpty(x)).Distinct());
    public List<string> IpAddresses { get; set; } = new();
    public List<IpEntry> IpEntries { get; set; } = new();
    public string DisplayName => IsIpGroup ? "IP Addresses" : Key;
    public int ConnectionCount => Flows.Count;
    public string StatusText => IsBlocked ? "Blocked" : Flows.Any(f => f.IsActive) ? "Active" : "Closed";
    public string ConnectionDetails => string.Join(" · ", Flows.Select(f => $"{f.RemoteIp}:{f.RemotePort} {f.Protocol} {f.Direction}").Distinct().Take(3));
    public void Refresh()
    {
        IsBlocked = Flows.Any(f => f.IsBlocked);
        var loc = Flows.Select(f => f.Location).FirstOrDefault(l => l != null);
        CountryCode = loc?.CountryCode;
        Country = loc?.Country;
        City = loc?.City;
        Region = loc?.Region;
        foreach (var n in new[] { nameof(LocationText), nameof(CountryCode), nameof(Flag), nameof(ConnectionCount), nameof(StatusText), nameof(ConnectionDetails), nameof(IpEntries) })
            OnPropertyChanged(n);
    }
}
public class IpEntry : NotifyModel
{
    public string Ip { get; set; } = "";
    public bool IsBlocked
    {
        get; set;
    }
    public string? CountryCode
    {
        get; set;
    }
    public string? Country
    {
        get; set;
    }
    public string? City
    {
        get; set;
    }
    public string Flag => CountryCode ?? "";
    public string LocationText => string.Join(" · ", new[] { Ip, City, Country }.Where(x => !string.IsNullOrEmpty(x)));
}
public class NetworkProcess : NotifyModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Pid
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    private bool _blocked, _expanded, _highlighted;
    public bool IsBlocked
    {
        get => _blocked; set
        {
            if (Set(ref _blocked, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }
    public bool IsExpanded
    {
        get => _expanded; set => Set(ref _expanded, value);
    }
    public bool IsHighlighted
    {
        get => _highlighted; set => Set(ref _highlighted, value);
    }
    public long BytesSent
    {
        get; set;
    }
    public long BytesReceived
    {
        get; set;
    }
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public List<NetworkFlow> Flows { get; set; } = new();
    public ObservableCollection<DestinationGroup> Destinations { get; } = new();
    private ImageSource? _icon;
    public ImageSource? AppIcon
    {
        get => _icon; set => Set(ref _icon, value);
    }
    public List<double> Sparkline { get; set; } = new();
    private readonly List<(long sent, long recv)> _samples = new();
    private long _prevSent, _prevRecv;
    public IReadOnlyList<(long sent, long recv)> BandwidthSamples => _samples;
    public long TotalSentRecent => _samples.Sum(s => s.sent);
    public long TotalRecvRecent => _samples.Sum(s => s.recv);
    public bool HasActivity => _samples.Any(s => s.sent > 0 || s.recv > 0);
    public int DestinationCount => Destinations.Sum(d => d.ConnectionCount);
    public bool IsAppBlocked
    {
        get; set;
    }
    public bool IsPending
    {
        get; set;
    }
    public string StatusText => IsPending ? "Awaiting approval" : IsAppBlocked || (Flows.Count > 0 && Flows.All(f => f.IsBlocked)) ? "Blocked" : IsBlocked ? "Partly blocked" : Flows.Any(f => f.IsActive) ? "Active" : "Closed";
    public string BytesSummary => $"↑ {Fmt(BytesSent)}  ↓ {Fmt(BytesReceived)}";
    public string SignatureText { get; set; } = "Checking signature…";
    public bool IsTrustedSystem
    {
        get; set;
    }
    public void RecordBandwidthDelta()
    {
        _samples.Add((Math.Max(0, BytesSent - _prevSent), Math.Max(0, BytesReceived - _prevRecv)));
        _prevSent = BytesSent;
        _prevRecv = BytesReceived;
        if (_samples.Count > 40)
            _samples.RemoveAt(0);
        OnPropertyChanged(nameof(BandwidthSamples));
        OnPropertyChanged(nameof(BytesSummary));
        OnPropertyChanged(nameof(StatusText));
    }
    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SignatureText));
        OnPropertyChanged(nameof(DestinationCount));
    }
    private static string Fmt(long b) => b < 1024 ? $"{b}B" : b < 1048576 ? $"{b / 1024d:F1}K" : b < 1073741824 ? $"{b / 1048576d:F1}M" : $"{b / 1073741824d:F1}G";
}
public class NetworkFlow : NotifyModel
{
    private bool _selected;
    public bool IsSelected
    {
        get => _selected; set => Set(ref _selected, value);
    }
    public Guid Id { get; set; } = Guid.NewGuid();
    public int DatabaseId
    {
        get; set;
    }
    public string? Hostname
    {
        get; set;
    }
    public HashSet<string> HostAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string RemoteIp { get; set; } = "";
    public int RemotePort
    {
        get; set;
    }
    public int LocalPort
    {
        get; set;
    }
    public FlowProtocol Protocol
    {
        get; set;
    }
    public FlowDirection Direction
    {
        get; set;
    }
    public long BytesSent
    {
        get; set;
    }
    public long BytesReceived
    {
        get; set;
    }
    public int ParentProcessId
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    private bool _active = true;
    public bool IsActive
    {
        get => _active; set
        {
            if (Set(ref _active, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }
    public bool WasDenied
    {
        get; set;
    }
    private bool _blocked;
    public bool IsBlocked
    {
        get => _blocked; set
        {
            if (Set(ref _blocked, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }
    public string? RegionCode
    {
        get; set;
    }
    private GeoLocation? _location;
    public GeoLocation? Location
    {
        get => _location; set
        {
            if (Set(ref _location, value))
            {
                OnPropertyChanged(nameof(LocationText));
                OnPropertyChanged(nameof(CountryCode));
            }
        }
    }
    public string CountryCode => Location?.CountryCode ?? "";
    public string LocationText => Location == null ? (System.Net.IPAddress.TryParse(RemoteIp, out var ip) && IpGeoService.IsPrivate(ip) ? "Local network" : "Location unavailable") : string.Join(" · ", new[] { Location.City, Location.Region, Location.Country }.Where(x => !string.IsNullOrEmpty(x)).Distinct());
    public string Destination => !string.IsNullOrEmpty(Hostname) ? $"{Hostname}:{RemotePort}" : $"{RemoteIp}:{RemotePort}";
    public string ProtocolText => Protocol.ToString();
    public string StatusText => IsBlocked ? "Blocked" : IsActive ? "Active" : "Closed";
    public string Details => $"{RemoteIp}:{RemotePort} · {Protocol} · {Direction}";
}

namespace EShield.Models;

public class AppSettings
{
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.AllowAll;
    public bool BlockListEnabled { get; set; } = true;
    public bool AllowSystemProcesses { get; set; } = true;
    public bool AllowDnsTraffic { get; set; } = true;
    public bool ShowStatusInTray { get; set; } = true;
    public bool ShowBandwidthInTray { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool SortAlphabetically { get; set; } = false;
    public bool DarkMode { get; set; } = true;
    public string TimeInterval { get; set; } = "Last1Hour";
    public int MaxFlowsPerProcess { get; set; } = 100;
}

public class TrafficPoint
{
    public DateTime Time
    {
        get; set;
    }
    public long BytesIn
    {
        get; set;
    }
    public long BytesOut
    {
        get; set;
    }
}

public class ApprovalRequest : System.ComponentModel.INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NetworkProcess Process { get; set; } = null!;
    public System.Collections.ObjectModel.ObservableCollection<PendingConnection> Connections { get; set; } = new();
    public DateTime RequestedAt { get; set; } = DateTime.Now;

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded; set
        {
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected; set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string BundleId => System.IO.Path.GetFileName(Process?.ExecutablePath ?? "");
    public int ConnectionCount => Connections.Count;
    public void Refresh() => OnPropertyChanged(nameof(ConnectionCount));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

public class PendingConnection : System.ComponentModel.INotifyPropertyChanged
{
    public string HostPort { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string? CountryCode
    {
        get; set;
    }
    public string? Country
    {
        get; set;
    }
    public NetworkFlow? Flow
    {
        get; set;
    }

    public string Flag => string.IsNullOrEmpty(CountryCode) || CountryCode.Length != 2 ? "🌐"
        : char.ConvertFromUtf32(0x1F1E6 + char.ToUpper(CountryCode[0]) - 'A') +
          char.ConvertFromUtf32(0x1F1E6 + char.ToUpper(CountryCode[1]) - 'A');

    public string LocationLine => Flow == null ? Ip : $"{Flow.Details} · {Flow.LocationText}";

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered; set
        {
            _isHovered = value;
            OnPropertyChanged();
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

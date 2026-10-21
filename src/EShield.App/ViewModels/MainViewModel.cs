using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using EShield.Models;
using EShield.Services;
namespace EShield.ViewModels;
public partial class MainViewModel : ObservableObject
{
    public readonly NetworkMonitorService Monitor;
    public readonly FirewallService Firewall;
    public readonly DatabaseService Database;
    public readonly BlockRuleService BlockRules;
    public readonly AllowRuleService AllowRules;
    public SettingsService Settings { get; }
    public readonly IpGeoService GeoService = new();
    public DnsResolverService DnsResolver => Monitor.Dns;
    public DnsCacheService DnsCache => Monitor.DnsCache;
    [ObservableProperty] private ObservableCollection<NetworkProcess> _processes = new();
    [ObservableProperty] private ObservableCollection<TrafficPoint> _trafficHistory = new();
    [ObservableProperty] private ObservableCollection<NetworkProcess> _filteredProcesses = new();
    [ObservableProperty] private NetworkProcess? _selectedProcess;
    [ObservableProperty] private NetworkFlow? _selectedFlow;
    [ObservableProperty] private string _currentPage = "Connections";
    [ObservableProperty] private ConnectionMode _connectionMode = ConnectionMode.AllowAll;
    [ObservableProperty] private string _filterMode = "All";
    [ObservableProperty] private string _statusText = "Starting…";
    [ObservableProperty] private string _bandwidthText = "↑ 0 B/s   ↓ 0 B/s";
    [ObservableProperty] private string _engineStatus = "Starting Windows filtering…";
    [ObservableProperty] private int _blockedCount;
    [ObservableProperty] private int _waitingCount;
    [ObservableProperty] private bool _isPro = true;
    [ObservableProperty] private bool _isMonitoring = true;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _timeRange = "5m";
    [ObservableProperty] private bool _hasNoConnections = true;
    [ObservableProperty] private string? _highlightedProcessName;
    [ObservableProperty] private ObservableCollection<ApprovalRequest> _pendingApprovals = new();
    [ObservableProperty] private ApprovalRequest? _selectedApproval;
    [ObservableProperty] private bool _isApprovalPanelVisible;
    public BlockListViewModel BlockListVM
    {
        get;
    }
    public AllowListViewModel AllowListVM
    {
        get;
    }
    public event Action<NetworkProcess, NetworkFlow?>? FocusRequested;
    public event Action<NetworkProcess?, NetworkFlow?, string?>? MapRequested;
    public event Action? ConnectionsChanged;
    private readonly DispatcherTimer _trafficTimer;
    private readonly DispatcherTimer _dropTimer;
    private string _sortMode = "Recent";
    private long _bytesInPerSec, _bytesOutPerSec;
    private bool _shutdown;
    [RelayCommand] public void Navigate(string page) => CurrentPage = page;
    public MainViewModel(bool startMonitoring = true, string? dataDirectory = null, bool enableFiltering = true)
    {
        Database = new(dataDirectory);
        Database.Initialize();
        Settings = new(dataDirectory);
        Settings.Load();
        Firewall = new(enableFiltering);
        BlockRules = new(Database, Firewall);
        AllowRules = new(Database, Firewall);
        BlockRules.Load();
        AllowRules.Load();
        BlockRules.IsEnabled = Settings.Settings.BlockListEnabled;
        Monitor = new();
        Monitor.NewConnection += OnNewConnection;
        Monitor.ProcessesUpdated += OnProcessesUpdated;
        Monitor.BandwidthUpdated += OnBandwidthUpdated;
        BlockListVM = new(BlockRules, this);
        AllowListVM = new(AllowRules, this);
        BlockRules.RulesChanged += RefreshPolicy;
        AllowRules.RulesChanged += RefreshPolicy;
        Firewall.AddressesResolved += () => Application.Current.Dispatcher.BeginInvoke(RefreshPolicy);
        PendingApprovals.CollectionChanged += (_, _) => { WaitingCount = PendingApprovals.Count; IsApprovalPanelVisible = ConnectionMode == ConnectionMode.RequireApproval && WaitingCount > 0; };
        _connectionMode = Settings.Settings.ConnectionMode;
        _trafficTimer = new()
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _trafficTimer.Tick += (_, _) => SaveTrafficBucket();
        _dropTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _dropTimer.Tick += (_, _) => { foreach (var drop in Firewall.DrainDrops()) Monitor.AddBlockedAttempt(drop); };
        RefreshPolicy();
        if (startMonitoring)
        {
            _trafficTimer.Start();
            _dropTimer.Start();
            Monitor.Start();
        }
    }
    partial void OnConnectionModeChanged(ConnectionMode value)
    {
        Settings.Settings.ConnectionMode = value;
        Settings.Save();
        RefreshPolicy();
        if (value != ConnectionMode.RequireApproval)
        {
            PendingApprovals.Clear();
            SelectedApproval = null;
        }
        IsApprovalPanelVisible = value == ConnectionMode.RequireApproval && WaitingCount > 0;
    }
    public void RefreshPolicy()
    {
        if (_shutdown)
            return;
        var blocks = BlockRules.IsEnabled ? BlockRules.Rules : Array.Empty<BlockRule>();
        Firewall.Configure(ConnectionMode, blocks, AllowRules.Rules, Processes, Settings.Settings);
        foreach (var request in PendingApprovals.Where(r => r.Connections.All(c => c.Flow != null && (!IsBlocked(r.Process, c.Flow) || BlockRules.IsBlocked(r.Process, c.Flow)))).ToArray())
            PendingApprovals.Remove(request);
        if (SelectedApproval != null && !PendingApprovals.Contains(SelectedApproval))
            SelectedApproval = PendingApprovals.FirstOrDefault();
        RefreshBlockedState();
        RefreshFilter();
        EngineStatus = Firewall.LastError != null ? $"Filtering unavailable: {Firewall.LastError}" : $"Windows Filtering Platform · {Monitor.Status}" + (Firewall.LastWarning != null ? $" · {Firewall.LastWarning}" : "");
        StatusText = Firewall.LastError != null ? "Filtering error" : ConnectionMode.ToString();
        ConnectionsChanged?.Invoke();
    }
    public bool IsBlocked(NetworkProcess proc, NetworkFlow flow)
    {
        if (BlockRules.IsBlocked(proc, flow))
            return true;
        if (ConnectionMode == ConnectionMode.AllowAll)
            return false;
        if (Settings.Settings.AllowDnsTraffic && flow.Protocol == FlowProtocol.UDP && flow.RemotePort == 53)
            return false;
        if (Settings.Settings.AllowSystemProcesses && proc.IsTrustedSystem)
            return false;
        return !AllowRules.IsAllowed(proc, flow);
    }
    private void OnNewConnection(NetworkProcess proc, NetworkFlow flow)
    {
        flow.Location = GeoService.LookupAsync(flow.RemoteIp).GetAwaiter().GetResult();
        flow.IsBlocked = IsBlocked(proc, flow);
        if (ConnectionMode == ConnectionMode.RequireApproval && flow.IsBlocked && !BlockRules.IsBlocked(proc, flow))
            EnqueueApproval(proc, flow);
    }
    private void OnProcessesUpdated(List<NetworkProcess> procs)
    {
        foreach (var p in procs)
            if (!Processes.Contains(p))
                Processes.Add(p);
        foreach (var p in procs)
            foreach (var flow in p.Flows)
            {
                flow.Location ??= GeoService.LookupAsync(flow.RemoteIp).GetAwaiter().GetResult();
                if (ConnectionMode == ConnectionMode.RequireApproval && IsBlocked(p, flow) && !BlockRules.IsBlocked(p, flow) && DateTime.Now - flow.LastSeen < TimeSpan.FromSeconds(5))
                    EnqueueApproval(p, flow);
            }
        RefreshPolicy();
    }
    private void OnBandwidthUpdated(long incoming, long outgoing)
    {
        _bytesInPerSec = incoming;
        _bytesOutPerSec = outgoing;
        BandwidthText = $"↑ {FormatBps(outgoing)}   ↓ {FormatBps(incoming)}";
    }
    private void EnqueueApproval(NetworkProcess proc, NetworkFlow flow)
    {
        var existing = PendingApprovals.FirstOrDefault(r => ReferenceEquals(r.Process, proc));
        if (existing == null)
        {
            existing = new()
            {
                Process = proc
            };
            PendingApprovals.Add(existing);
            SelectedApproval ??= existing;
        }
        if (existing.Connections.Any(c => c.Ip == flow.RemoteIp && c.Flow?.RemotePort == flow.RemotePort))
            return;
        existing.Connections.Add(new()
        {
            HostPort = flow.Destination,
            Ip = flow.RemoteIp,
            Flow = flow,
            CountryCode = flow.Location?.CountryCode,
            Country = flow.Location?.Country
        });
        existing.Refresh();
    }
    [RelayCommand]
    public void ApproveSelected()
    {
        if (SelectedApproval == null)
            return;
        var selected = SelectedApproval;
        AllowRules.Add(AllowRule.FromProcess(selected.Process));
        PendingApprovals.Remove(selected);
        SelectedApproval = PendingApprovals.FirstOrDefault();
        RefreshPolicy();
    }
    [RelayCommand]
    public void BlockSelected()
    {
        if (SelectedApproval == null)
            return;
        var selected = SelectedApproval;
        BlockRules.Add(BlockRule.FromProcess(selected.Process));
        PendingApprovals.Remove(selected);
        SelectedApproval = PendingApprovals.FirstOrDefault();
        RefreshPolicy();
    }
    [RelayCommand]
    public void ApproveAll()
    {
        foreach (var r in PendingApprovals.ToArray())
            AllowRules.Add(AllowRule.FromProcess(r.Process));
        PendingApprovals.Clear();
        SelectedApproval = null;
        RefreshPolicy();
    }
    [RelayCommand]
    public void BlockAll()
    {
        foreach (var r in PendingApprovals.ToArray())
            BlockRules.Add(BlockRule.FromProcess(r.Process));
        PendingApprovals.Clear();
        SelectedApproval = null;
        RefreshPolicy();
    }
    partial void OnFilterModeChanged(string value) => RefreshFilter();
    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnTimeRangeChanged(string value) => RefreshFilter();
    private bool RefreshBlockedState()
    {
        bool changed = false;
        foreach (var proc in Processes)
        {
            foreach (var flow in proc.Flows)
                flow.IsBlocked = IsBlocked(proc, flow);
            var blocked = proc.Flows.Any(f => f.IsBlocked);
            changed |= proc.IsBlocked != blocked;
            proc.IsBlocked = blocked;
            proc.IsAppBlocked = BlockRules.IsEnabled && BlockRules.Rules.Any(r => string.IsNullOrEmpty(r.Domain) && !string.IsNullOrEmpty(r.ExecutablePath) && r.ExecutablePath.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            proc.IsPending = PendingApprovals.Any(r => ReferenceEquals(r.Process, proc));
            foreach (var d in proc.Destinations)
                d.Refresh();
            proc.Refresh();
        }
        BlockedCount = Processes.Count(p => p.IsBlocked);
        return changed;
    }
    private void RefreshFilter()
    {
        var since = TimeRange switch
        {
            "1m" => DateTime.Now.AddMinutes(-1),
            "5m" => DateTime.Now.AddMinutes(-5),
            "15m" => DateTime.Now.AddMinutes(-15),
            "1h" => DateTime.Now.AddHours(-1),
            _ => DateTime.MinValue
        };
        var q = Processes.Where(p => p.LastSeen >= since);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            q = q.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || p.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Flows.Any(f => f.RemoteIp.Contains(term, StringComparison.OrdinalIgnoreCase) || f.Hostname?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || f.HostAliases.Any(h => h.Contains(term, StringComparison.OrdinalIgnoreCase)) || f.LocationText.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        q = FilterMode switch
        {
            "Blocked" => q.Where(p => p.IsBlocked),
            "Allowed" => q.Where(p => p.Flows.Any(f => !f.IsBlocked)),
            _ => q
        };
        var desired = (_sortMode == "Alphabetical" ? q.OrderBy(p => p.Name) : q.OrderByDescending(p => p.LastSeen)).ToList();
        for (int i = FilteredProcesses.Count - 1; i >= 0; i--)
            if (!desired.Contains(FilteredProcesses[i]))
                FilteredProcesses.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            var index = FilteredProcesses.IndexOf(desired[i]);
            if (index < 0)
                FilteredProcesses.Insert(i, desired[i]);
            else if (index != i && _sortMode == "Alphabetical")
                FilteredProcesses.Move(index, i);
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                desired[i].IsExpanded = true;
                foreach (var d in desired[i].Destinations)
                    d.IsExpanded = true;
            }
        }
        HasNoConnections = FilteredProcesses.Count == 0;
    }
    public void HighlightProcessByName(string name) => FocusConnection(Processes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), null);
    public void FocusConnection(NetworkProcess? proc, NetworkFlow? flow)
    {
        if (proc == null)
            return;
        SearchText = "";
        FilterMode = "All";
        TimeRange = "All";
        SelectedProcess = proc;
        SelectedFlow = flow;
        foreach (var p in Processes)
        {
            p.IsHighlighted = ReferenceEquals(p, proc);
            foreach (var f in p.Flows)
                f.IsSelected = ReferenceEquals(f, flow);
            foreach (var d in p.Destinations)
            {
                d.IsSelected = ReferenceEquals(p, proc) && flow != null && d.Flows.Contains(flow);
                if (d.IsSelected)
                    d.IsExpanded = true;
            }
        }
        proc.IsExpanded = true;
        HighlightedProcessName = proc.Name;
        RefreshFilter();
        FocusRequested?.Invoke(proc, flow);
    }
    public void ShowInMap(NetworkProcess? proc, NetworkFlow? flow = null, string? domain = null) => MapRequested?.Invoke(proc, flow, domain);
    public void BlockDomain(string domain, NetworkProcess proc)
    {
        if (string.IsNullOrEmpty(proc.ExecutablePath))
        {
            MessageBox.Show("The executable path is unavailable. Add a global destination rule from Block List instead.");
            return;
        }
        BlockRules.Add(new()
        {
            Domain = RuleMatcher.NormalizeTarget(domain),
            AppName = proc.Name,
            ExecutablePath = proc.ExecutablePath,
            RuleType = RuleType.Both
        });
    }
    public void UnblockDestination(string domain, NetworkProcess proc)
    {
        foreach (var r in BlockRules.Rules.Where(r => r.Domain?.Equals(domain, StringComparison.OrdinalIgnoreCase) == true && r.ExecutablePath?.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase) == true).ToArray())
            BlockRules.Remove(r.Id);
    }
    public void AllowDestination(string domain, NetworkProcess proc) => AllowRules.Add(new() { Domain = RuleMatcher.NormalizeTarget(domain), ExecutablePath = proc.ExecutablePath, AppName = proc.Name, RuleType = RuleType.Both });
    [RelayCommand]
    public void ToggleSortMode()
    {
        _sortMode = _sortMode == "Alphabetical" ? "Recent" : "Alphabetical";
        RefreshFilter();
    }
    [RelayCommand]
    public void BlockProcess(NetworkProcess proc)
    {
        if (!string.IsNullOrEmpty(proc.ExecutablePath))
            BlockRules.Add(BlockRule.FromProcess(proc));
    }
    [RelayCommand]
    public void AllowProcess(NetworkProcess proc)
    {
        AllowRules.Add(AllowRule.FromProcess(proc));
        RefreshPolicy();
    }
    [RelayCommand]
    public void UnblockProcess(NetworkProcess proc)
    {
        foreach (var r in BlockRules.Rules.Where(r => r.ExecutablePath?.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase) == true).ToArray())
            BlockRules.Remove(r.Id);
        RefreshPolicy();
    }
    [RelayCommand]
    public void UnblockAll()
    {
        foreach (var id in BlockRules.Rules.Select(r => r.Id).ToArray())
            BlockRules.Remove(id);
        ConnectionMode = ConnectionMode.AllowAll;
        PendingApprovals.Clear();
        SelectedApproval = null;
        RefreshPolicy();
    }
    [RelayCommand]
    public void ClearAllActivity()
    {
        if (MessageBox.Show("Clear recorded connections? Your rules and settings will be kept.", "Clear Network Activity", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        Monitor.ClearHistory();
        Database.ClearAllActivity();
        Processes.Clear();
        TrafficHistory.Clear();
        PendingApprovals.Clear();
        SelectedApproval = null;
        RefreshPolicy();
    }
    private void SaveTrafficBucket()
    {
        if (_shutdown)
            return;
        Database.InsertTrafficBucket(DateTime.Now, _bytesInPerSec, _bytesOutPerSec);
        var recent = Database.GetTrafficHistory(DateTime.Now.AddHours(-1));
        TrafficHistory.Clear();
        foreach (var p in recent)
            TrafficHistory.Add(p);
    }
    private static string FormatBps(long b) => b < 1024 ? $"{b} B/s" : b < 1048576 ? $"{b / 1024d:F1} KB/s" : $"{b / 1048576d:F1} MB/s";
    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;
        _trafficTimer.Stop();
        _dropTimer.Stop();
        Monitor.Stop();
        Firewall.Dispose();
        Database.Dispose();
        if (ConnectionMode == ConnectionMode.RequireApproval)
        {
            Settings.Settings.ConnectionMode = ConnectionMode.AllowAll;
            Settings.Save();
        }
    }
}

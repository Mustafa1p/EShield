using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EShield.Behaviors;
using EShield.Models;
using EShield.ViewModels;
using H.NotifyIcon;

namespace EShield.Views;

public partial class MainWindow : Window
{
    private TaskbarIcon? _tray;
    private string _timeRange = "5m";
    private MapWindow? _map;
    private ApprovalWindow? _approval;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // R08 Ctrl+,
        InputBindings.Add(new KeyBinding(AppKeyBindings.NavigateSettings,
            Key.OemComma, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(AppKeyBindings.NavigateSettings,
            (_, _) => OpenSettings()));

        // R20 Ctrl+F
        InputBindings.Add(new KeyBinding(AppKeyBindings.FocusSearch,
            Key.F, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(AppKeyBindings.FocusSearch,
            (_, _) => SearchBox.Focus()));

        // R29
        CommandBindings.Add(new CommandBinding(AppKeyBindings.QuitSwitchAllowAll,
            (_, _) => QuitAndSwitch()));
    }

    private void OnLoaded(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        vm.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(MainViewModel.ConnectionMode))
                UpdateModePill(vm);
            if (a.PropertyName == nameof(MainViewModel.FilterMode))
                UpdateFilterPills(vm.FilterMode);
            if (a.PropertyName == nameof(MainViewModel.TimeRange))
                TimeRangeText.Text = vm.TimeRange;
            if (a.PropertyName == nameof(MainViewModel.WaitingCount) && vm.WaitingCount > 0)
            {
                _approval ??= new ApprovalWindow { DataContext = vm, Owner = this };
                if (!_approval.IsVisible)
                    _approval.Show();
            }
            if (a.PropertyName == nameof(MainViewModel.WaitingCount) && vm.WaitingCount == 0)
                _approval?.Hide();
        };
        vm.MapRequested += (proc, flow, domain) => { OpenMap(); _map?.FocusTraffic(proc, flow, domain); };
        UpdateModePill(vm);
        SetupTray(vm);
        SearchBox.Focus(); // R25
    }

    // ── Mode pill ─────────────────────────────────────────────────────────
    private void UpdateModePill(MainViewModel vm)
    {
        var (label, color) = vm.ConnectionMode switch
        {
            ConnectionMode.AllowAll => ("Allow All", Color.FromRgb(34, 197, 94)),
            ConnectionMode.RequireApproval => ("Approval", Color.FromRgb(245, 158, 11)),
            ConnectionMode.BlockAll => ("Block All", Color.FromRgb(239, 68, 68)),
            _ => ("Allow All", Color.FromRgb(34, 197, 94))
        };
        ModePillText.Text = label;
        ModePill.Background = new SolidColorBrush(color);
    }

    private void ModePill_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        var menu = new ContextMenu();
        foreach (var (lbl, mode) in new[] {
            ("⚡  Allow All",        ConnectionMode.AllowAll),
            ("⏳  Require Approval", ConnectionMode.RequireApproval),
            ("🚫  Block All",        ConnectionMode.BlockAll) })
        {
            var m = mode;
            var l = lbl;
            var item = new MenuItem
            {
                Header = l,
                FontWeight = vm.ConnectionMode == m ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (_, _) => vm.ConnectionMode = m;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── Header buttons ────────────────────────────────────────────────────
    private void BlockList_Click(object s, RoutedEventArgs e) => OpenBlockList();
    private void AllowList_Click(object s, RoutedEventArgs e) => OpenAllowList();
    private void Settings_Click(object s, RoutedEventArgs e) => OpenSettings();
    private void Map_Click(object s, RoutedEventArgs e) => OpenMap();

    private void OpenBlockList()
    {
        if (DataContext is MainViewModel vm)
            new BlockListWindow { DataContext = vm.BlockListVM, Owner = this }.Show();
    }
    private void OpenAllowList()
    {
        if (DataContext is MainViewModel vm)
            new AllowListWindow { DataContext = vm.AllowListVM, Owner = this }.Show();
    }
    private void OpenSettings()
    {
        if (DataContext is MainViewModel vm)
            new SettingsWindow { DataContext = vm, Owner = this }.ShowDialog();
    }

    private void OpenMap()
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (_map == null)
        {
            _map = new MapWindow { DataContext = vm, Owner = this };
            _map.Closed += (_, _) => _map = null;
            _map.Show();
        }
        else
            _map.Activate();
    }

    // ── Filter pills ──────────────────────────────────────────────────────
    private void Filter_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && DataContext is MainViewModel vm)
        {
            vm.FilterMode = btn.Tag?.ToString() ?? "All";
            UpdateFilterPills(vm.FilterMode);
        }
    }

    private void UpdateFilterPills(string mode)
    {
        var accent = new SolidColorBrush(Color.FromRgb(32, 120, 255));
        PillAll.Background = mode == "All" ? accent : Brushes.Transparent;
        PillBlocked.Background = mode == "Blocked" ? accent : Brushes.Transparent;
        PillAllowed.Background = mode == "Allowed" ? accent : Brushes.Transparent;
    }

    // ── Time range ────────────────────────────────────────────────────────
    private void TimeRange_Click(object s, RoutedEventArgs e)
    {
        var ranges = new[] { "1m", "5m", "15m", "1h", "All" };
        var menu = new ContextMenu();
        foreach (var r in ranges)
        {
            var range = r;
            var item = new MenuItem
            {
                Header = r,
                FontWeight = r == _timeRange ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (_, _) => { _timeRange = range; TimeRangeText.Text = range; if (DataContext is MainViewModel vm) vm.TimeRange = range; };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── Title bar ─────────────────────────────────────────────────────────
    // Title bar drag is handled natively by WindowChrome.CaptionHeight=44.
    // This method is kept for compatibility with any lingering XAML reference.
    private void TitleBar_DragMove(object s, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            try
            {
                DragMove();
            }
            catch { }
    }
    private void Close_Click(object s, MouseButtonEventArgs e) => Close();
    private void Minimize_Click(object s, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, MouseButtonEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ── Tray ──────────────────────────────────────────────────────────────
    private void SetupTray(MainViewModel vm)
    {
        try
        {
            _tray = new TaskbarIcon
            {
                ToolTipText = "EShield",
                Visibility = Visibility.Visible,
                IconSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/EShield;component/Resources/shield.ico"))
            };
            _tray.TrayMouseDoubleClick += (_, _) => { Show(); Activate(); WindowState = WindowState.Normal; };

            var menu = new ContextMenu();
            var h = new MenuItem { Header = "EShield", IsEnabled = false, FontWeight = FontWeights.Bold };
            menu.Items.Add(h);
            menu.Items.Add(new Separator());

            void Add(string hdr, Action act)
            {
                var it = new MenuItem { Header = hdr };
                it.Click += (_, _) => act();
                menu.Items.Add(it);
            }

            Add("Show Window", () => { Show(); Activate(); });
            Add("Block List", OpenBlockList);
            Add("Allow List", OpenAllowList);
            Add("Settings", OpenSettings);
            menu.Items.Add(new Separator());

            // Emergency unblock — always visible
            var unblockAll = new MenuItem
            {
                Header = "⚡  Unblock All Apps",
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.LimeGreen
            };
            unblockAll.Click += (_, _) =>
            {
                if (DataContext is MainViewModel vm2)
                    vm2.UnblockAllCommand.Execute(null);
            };
            menu.Items.Add(unblockAll);
            menu.Items.Add(new Separator());

            // R29
            Add("Quit & Switch to Allow All", QuitAndSwitch);
            menu.Items.Add(new Separator());
            Add("Quit EShield", () =>
            {
                if (vm.ConnectionMode == ConnectionMode.RequireApproval)
                {
                    var r = MessageBox.Show(
                        "Require Approval only works while EShield is running.\nQuit and switch to Allow All?",
                        "Quit EShield", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (r == MessageBoxResult.Cancel)
                        return;
                    if (r == MessageBoxResult.Yes)
                        vm.ConnectionMode = ConnectionMode.AllowAll;
                }
                vm.Shutdown();
                Application.Current.Shutdown();
            });
            _tray.ContextMenu = menu;
        }
        catch { }
    }

    private void QuitAndSwitch()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ConnectionMode = ConnectionMode.AllowAll;
            vm.Shutdown();
        }
        Application.Current.Shutdown();
    }

    // ── Window closing ────────────────────────────────────────────────────
    private void Window_Closing(object s, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Settings.Settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _tray?.Dispose();
        if (DataContext is MainViewModel v)
            v.Shutdown();
        Application.Current.Shutdown();
    }
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using EShield.ViewModels;
namespace EShield;
public partial class App : Application
{
    public static MainViewModel ViewModel { get; private set; } = null!;
    private Mutex? _instance;
    private EventWaitHandle? _activate;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--replace"))
            foreach (var p in Process.GetProcessesByName("EShield"))
                using (p)
                {
                    if (p.Id == Environment.ProcessId)
                        continue;
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }
        _instance = new Mutex(true, @"Local\EShield.MainWindow", out var created);
        if (!created)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(@"Local\EShield.Activate");
                signal.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\EShield.Activate");
        DispatcherUnhandledException += (_, a) => { Log(a.Exception); };
        try
        {
            // One-time migration: remove broad legacy rules replaced by scoped WFP filters.
            dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
            var obsolete = new List<string>();
            foreach (dynamic rule in policy.Rules)
            {
                string name = rule.Name;
                if (name.StartsWith("[EShield] Block", StringComparison.Ordinal) || name.StartsWith("[EShield] Allow", StringComparison.Ordinal))
                    obsolete.Add(name);
            }
            foreach (var name in obsolete)
                policy.Rules.Remove(name);
        }
        catch { }
        try
        {
            ViewModel = new MainViewModel();
            if (ViewModel.Settings.Settings.LaunchAtStartup)
                ViewModel.Settings.SetLaunchAtStartup(true);
            var win = new Views.MainWindow { DataContext = ViewModel };
            win.Show();
            MainWindow = win;
            _ = Task.Run(() => { while (_activate != null) { try { if (_activate.WaitOne(1000)) Dispatcher.BeginInvoke(() => { win.Show(); win.WindowState = WindowState.Normal; win.Activate(); }); } catch (ObjectDisposedException) { break; } } });
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) =>
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EShield");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "runtime-status.json"), JsonSerializer.Serialize(new
                {
                    Pid = Environment.ProcessId,
                    Version = typeof(App).Assembly.GetName().Version?.ToString(),
                    WfpAvailable = ViewModel.Firewall.IsAvailable,
                    WfpError = ViewModel.Firewall.LastError,
                    PersistentRulesWarning = ViewModel.Firewall.LastWarning,
                    DropTelemetry = ViewModel.Firewall.HasDropTelemetry,
                    EtwAvailable = ViewModel.Monitor.IsEventMonitoring,
                    MonitorStatus = ViewModel.Monitor.Status,
                    DnsTracing = ViewModel.Monitor.IsDnsTracing,
                    UdpConnections = ViewModel.Processes.Sum(p => p.Flows.Count(f => f.Protocol == Models.FlowProtocol.UDP)),
                    Ipv6Connections = ViewModel.Processes.Sum(p => p.Flows.Count(f => f.RemoteIp.Contains(':'))),
                    GeoDatabase = ViewModel.GeoService.IsAvailable,
                    Apps = ViewModel.Processes.Count,
                    Connections = ViewModel.Processes.Sum(p => p.Flows.Count),
                    Located = ViewModel.Processes.Sum(p => p.Flows.Count(f => f.Location != null)),
                    Named = ViewModel.Processes.Sum(p => p.Flows.Count(f => !string.IsNullOrEmpty(f.Hostname))),
                    Time = DateTimeOffset.Now
                }));
            };
            timer.Start();
        }
        catch (Exception ex) { Log(ex); MessageBox.Show(ex.Message, "EShield startup failed"); Shutdown(1); }
    }
    private static void Log(Exception e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EShield");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "last-error.log"), e.ToString());
        }
        catch { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        ViewModel?.Shutdown();
        _activate?.Dispose();
        _activate = null;
        try
        {
            _instance?.ReleaseMutex();
        }
        catch { }
        _instance?.Dispose();
        base.OnExit(e);
    }
}

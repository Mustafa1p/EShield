using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EShield.Models;
using EShield.Services;
using EShield.ViewModels;
using EShield.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// Public documentation fixtures only. Never read real processes, network
// connections, user settings, executable paths, or the current desktop here.
internal static class DemoScreenshots
{
    public static void Capture(string output)
    {
        Directory.CreateDirectory(output);
        var state = Path.Combine(Path.GetTempPath(), "EShield-demo", Guid.NewGuid().ToString("N"));
        var vm = new MainViewModel(false, state, false);
        try
        {
            var browser = Process("Example Browser", @"C:\ExampleApps\Browser\Browser.exe", 4242);
            var chat = Process("Example Chat", @"C:\ExampleApps\Chat\Chat.exe", 4243);
            var updater = Process("Example Updater", @"C:\ExampleApps\Updater\Updater.exe", 4244);
            Add(browser, "www.example.com", "192.0.2.10", 443, FlowProtocol.TCP, new(40.71, -74.01, "United States", "New York", "US", "New York"));
            Add(browser, "assets.example.net", "198.51.100.20", 443, FlowProtocol.TCP, new(52.37, 4.90, "Netherlands", "Amsterdam", "NL", "North Holland"));
            Add(browser, "telemetry.example.org", "203.0.113.30", 443, FlowProtocol.UDP, new(35.68, 139.69, "Japan", "Tokyo", "JP", "Tokyo"));
            Add(chat, "chat.example.net", "192.0.2.40", 443, FlowProtocol.TCP, new(51.51, -0.13, "United Kingdom", "London", "GB", "England"));
            Add(updater, "updates.example.org", "198.51.100.50", 443, FlowProtocol.TCP, new(50.11, 8.68, "Germany", "Frankfurt", "DE", "Hesse"));
            foreach (var p in new[] { browser, chat, updater })
                vm.Processes.Add(p);
            vm.RefreshPolicy();
            DemoStatus(vm);
            foreach (var d in browser.Destinations)
                d.IsExpanded = false;
            browser.IsExpanded = true;
            chat.IsExpanded = false;
            updater.IsExpanded = false;
            RenderWindow(new MainWindow { DataContext = vm }, output, "connections", 780, 790);

            vm.SearchText = "example";
            DemoStatus(vm);
            foreach (var d in browser.Destinations)
                d.IsExpanded = false;
            foreach (var d in chat.Destinations)
                d.IsExpanded = false;
            foreach (var d in updater.Destinations)
                d.IsExpanded = false;
            RenderWindow(new MainWindow { DataContext = vm }, output, "search", 780, 980);
            vm.SearchText = "";
            chat.IsExpanded = false;
            updater.IsExpanded = false;

            vm.BlockDomain("telemetry.example.org", browser);
            vm.BlockRules.Add(new()
            {
                ExecutablePath = updater.ExecutablePath,
                AppName = updater.Name,
                RuleType = RuleType.App
            });
            DemoStatus(vm);
            RenderWindow(new MainWindow { DataContext = vm }, output, "destination-blocking", 780, 825);
            RenderWindow(new BlockListWindow { DataContext = vm.BlockListVM }, output, "block-list", 620, 530);
            vm.AllowDestination("assets.example.net", browser);
            vm.AllowRules.Add(AllowRule.FromProcess(chat));
            RenderWindow(new AllowListWindow { DataContext = vm.AllowListVM }, output, "allow-list", 620, 530);

            var bandwidth = new BandwidthPopupWindow();
            bandwidth.Populate(browser, null!);
            RenderWindow(bandwidth, output, "bandwidth", 520, 490);
            RenderWindow(new SettingsWindow { DataContext = vm }, output, "settings", 620, 780);

            vm.ConnectionMode = ConnectionMode.RequireApproval;
            var pending = new ApprovalRequest { Process = browser };
            foreach (var flow in browser.Flows.Take(2))
                pending.Connections.Add(new()
                {
                    HostPort = flow.Destination,
                    Ip = flow.RemoteIp,
                    Flow = flow,
                    CountryCode = flow.CountryCode,
                    Country = flow.Location!.Country
                });
            vm.PendingApprovals.Add(pending);
            vm.SelectedApproval = pending;
            RenderWindow(new ApprovalWindow { DataContext = vm }, output, "approval", 650, 800);
            vm.ConnectionMode = ConnectionMode.AllowAll;
            vm.PendingApprovals.Clear();
            DemoStatus(vm);
            CaptureMap(vm, output, state);
            File.WriteAllText(Path.Combine(output, "manifest.json"), """
{
  "data": "synthetic examples only",
  "apps": "fictional Example apps",
  "domains": "RFC 2606 example domains",
  "addresses": "RFC 5737 documentation ranges",
  "locations": "illustrative fixture coordinates, not geolocation claims about documentation addresses",
  "capture": "native EShield views rendered from isolated demo state; no real desktop or live connection capture"
}
""");
            Console.WriteLine("PASS: generated 9 public screenshots using synthetic data only.");
        }
        finally { vm.Shutdown(); }
    }
    private static NetworkProcess Process(string name, string path, int pid)
    {
        var p = new NetworkProcess { Name = name, ExecutablePath = path, Pid = pid, SignatureText = "Example Software · synthetic publisher data" };
        for (int i = 0; i < 30; i++)
        {
            p.BytesSent += 8000 + (i % 7) * 1600;
            p.BytesReceived += 25000 + (i % 9) * 11000;
            p.RecordBandwidthDelta();
        }
        return p;
    }
    private static void Add(NetworkProcess p, string host, string ip, int port, FlowProtocol protocol, GeoLocation location)
    {
        var f = new NetworkFlow { Hostname = host, RemoteIp = ip, RemotePort = port, Protocol = protocol, Location = location, ParentProcessId = p.Pid, BytesSent = 48520, BytesReceived = 240200, Direction = EShield.Models.FlowDirection.Outbound };
        f.HostAliases.Add(host);
        p.Flows.Add(f);
        var d = new DestinationGroup { Key = host, Flows = new() { f } };
        d.Refresh();
        p.Destinations.Add(d);
    }
    private static void DemoStatus(MainViewModel vm)
    {
        vm.EngineStatus = "DEMO · synthetic data only · monitoring and filtering disabled";
        vm.BandwidthText = "↑ 24 KB/s   ↓ 180 KB/s   ·   example traffic";
    }
    private static void RenderWindow(Window window, string directory, string name, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = window.DataContext;
        var root = new Grid { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)) };
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(38)
        });
        root.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(17, 43, 35)), Child = new TextBlock { Text = "DEMO  /  EXAMPLE DATA ONLY", FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172)), Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } });
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
    private static void CaptureMap(MainViewModel vm, string output, string state)
    {
        var frame = new DispatcherFrame();
        Exception? failure = null;
        var owner = new Window { Width = 300, Height = 720, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new TextBlock { Text = "Generating example-only documentation images…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) } };
        var map = new MapWindow(Path.Combine(state, "map")) { DataContext = vm, Width = 960 };
        owner.Loaded += async (_, _) =>
        {
            try
            {
                map.Owner = owner;
                map.Show();
                var web = (WebView2)map.FindName("MapView");
                bool ready = false;
                for (int i = 0; i < 100; i++)
                {
                    await Task.Delay(200);
                    if (web.CoreWebView2 != null && await web.CoreWebView2.ExecuteScriptAsync("typeof window.setConnections === 'function'") == "true")
                    {
                        ready = true;
                        break;
                    }
                }
                if (!ready)
                    throw new Exception("The synthetic map could not initialize.");
                await Task.Delay(1700);
                await web.CoreWebView2!.ExecuteScriptAsync("document.getElementById('hint').textContent='DEMO / EXAMPLE DATA ONLY — illustrative destination locations'; map.setView([28,20],2); var first; pins.eachLayer(function(m){if(!first)first=m;}); if(first)first.openPopup();");
                await Task.Delay(700);
                using var file = File.Create(Path.Combine(output, "map.png"));
                await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            }
            catch (Exception e) { failure = e; }
            finally { map.Close(); owner.Close(); frame.Continue = false; }
        };
        owner.Show();
        Dispatcher.PushFrame(frame);
        if (failure != null)
            throw failure;
    }
}

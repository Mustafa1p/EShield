using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EShield.Models;
using EShield.Services;
using EShield.ViewModels;
using EShield.Views;

internal static class Program
{
    private static int _passed;
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--persistence"))
        {
            using var log = new StreamWriter(Path.Combine(args[0], "persistence-test.log")) { AutoFlush = true };
            Console.SetOut(log);
            RunPersistentCheck();
            return;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var theme = new ModernWpf.ThemeResources();
        theme.BeginInit();
        theme.RequestedTheme = ModernWpf.ApplicationTheme.Dark;
        app.Resources.MergedDictionaries.Add(theme);
        theme.EndInit();
        app.Resources.MergedDictionaries.Add(new ModernWpf.Controls.XamlControlsResources());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/EShield;component/Themes/DarkTheme.xaml") });
        foreach (var pair in new[] { "BoolVis=BoolToVisibilityConverter", "InvBoolVis=InverseBoolToVisibilityConverter", "BlockColor=BlockedToColorConverter", "BlockBadge=BlockedToBadgeConverter", "ModeColor=ConnectionModeToColorConverter", "ModeStr=ConnectionModeToStringConverter", "IntVis=IntToVisibilityConverter", "NullVis=NullToVisibilityConverter", "InvNullVis=InverseNullToVisibilityConverter", "ExpandArrow=ExpandArrowConverter", "DestFore=DestinationForegroundConverter", "HighlightBrush=HighlightBrushConverter", "NameToColor=NameToColorConverter", "InitialConverter=InitialConverter", "CountryFlag=CountryFlagConverter" })
        {
            var parts = pair.Split('=');
            app.Resources[parts[0]] = Activator.CreateInstance(typeof(EShield.Converters.CountryFlagConverter).Assembly.GetType("EShield.Converters." + parts[1])!)!;
        }
        if (args.Contains("--screenshots"))
        {
            DemoScreenshots.Capture(args[0]);
            return;
        }
        var dir = Path.Combine(Path.GetTempPath(), "EShield-tests", Guid.NewGuid().ToString("N"));
        var vm = new MainViewModel(false, dir, false);
        try
        {
            var chrome = new NetworkProcess { Name = "chrome", ExecutablePath = Environment.ProcessPath! };
            var other = new NetworkProcess { Name = "Other browser", ExecutablePath = @"C:\other.exe" };
            var wiki = new NetworkFlow { Hostname = "en.wikipedia.org", RemoteIp = "208.80.154.224", RemotePort = 443 };
            var upload = new NetworkFlow { Hostname = "upload.wikimedia.org", RemoteIp = "91.198.174.208", RemotePort = 443 };
            var unrelated = new NetworkFlow { Hostname = "example.org", RemoteIp = "93.184.215.14", RemotePort = 443 };
            chrome.Flows.AddRange(new[] { wiki, upload, unrelated });
            chrome.Destinations.Add(new()
            {
                Key = wiki.Hostname,
                Flows = new() { wiki }
            });
            chrome.Destinations.Add(new()
            {
                Key = upload.Hostname,
                Flows = new() { upload }
            });
            chrome.Destinations.Add(new()
            {
                Key = unrelated.Hostname,
                Flows = new() { unrelated }
            });
            other.Flows.Add(new()
            {
                Hostname = wiki.Hostname,
                RemoteIp = wiki.RemoteIp,
                RemotePort = 443
            });
            vm.Processes.Add(chrome);
            vm.Processes.Add(other);
            vm.RefreshPolicy();
            vm.BlockDomain(wiki.Hostname, chrome);
            Assert(wiki.IsBlocked && !upload.IsBlocked && !unrelated.IsBlocked && !other.Flows[0].IsBlocked, "one domain does not block its app or another app");
            Assert(chrome.StatusText == "Partly blocked", "partial-block status distinguishes app policy");
            var specs = FirewallService.BuildPolicy(ConnectionMode.AllowAll, vm.BlockRules.Rules, vm.AllowRules.Rules, vm.Processes, vm.Settings.Settings, _ => Array.Empty<string>());
            Assert(specs.All(s => s.App == chrome.ExecutablePath && s.Ip == wiki.RemoteIp && !s.Permit), "kernel policy has app AND destination conditions");
            vm.UnblockDestination(wiki.Hostname, chrome);
            Assert(!wiki.IsBlocked && vm.BlockedCount == 0, "destination removal refreshes status");
            vm.BlockDomain(upload.RemoteIp, chrome);
            Assert(upload.IsBlocked && !wiki.IsBlocked, "IP rules stay narrow");
            vm.UnblockAll();
            Assert(!RuleMatcher.MatchesHost("wikipedia.org", "wikipedia.org.evil.test", "1.1.1.1"), "suffix spoofing rejected");
            Assert(RuleMatcher.MatchesHost("*.wikipedia.org", "en.wikipedia.org", "1.1.1.1") && !RuleMatcher.MatchesHost("*.wikipedia.org", "evilwikipedia.org", "1.1.1.1"), "wildcard respects domain boundaries");
            Assert(RuleMatcher.NormalizeTarget("https://EN.Wikipedia.org/wiki/Test") == "en.wikipedia.org", "website URL normalized to hostname");
            vm.SearchText = "wiki";
            Assert(vm.FilteredProcesses.Contains(chrome) && chrome.IsExpanded && chrome.Destinations.Count == 3, "search opens app and retains every destination");
            vm.SearchText = upload.RemoteIp;
            Assert(vm.FilteredProcesses.Count == 1, "IP search");
            vm.SearchText = "no-such-app";
            vm.FilterMode = "Blocked";
            var focused = false;
            vm.FocusRequested += (p, f) => focused = ReferenceEquals(p, chrome) && ReferenceEquals(f, upload);
            vm.FocusConnection(chrome, upload);
            Assert(focused && vm.FilterMode == "All" && vm.SearchText == "" && chrome.IsExpanded && upload.IsSelected, "map opens exact connection through active filters");
            vm.BlockProcess(chrome);
            Assert(chrome.Flows.All(f => f.IsBlocked) && !other.Flows[0].IsBlocked, "whole-app rule isolates executable");
            vm.FilterMode = "Blocked";
            vm.UnblockAll();
            Assert(vm.BlockedCount == 0 && vm.FilteredProcesses.Count == 0, "unblock all clears banner and blocked filter immediately");
            vm.FilterMode = "All";
            vm.ConnectionMode = ConnectionMode.BlockAll;
            Assert(wiki.IsBlocked && other.Flows[0].IsBlocked, "default-deny presentation");
            vm.AllowDestination(wiki.Hostname, chrome);
            Assert(!wiki.IsBlocked && upload.IsBlocked, "destination exception in default deny");
            vm.BlockDomain(wiki.Hostname, chrome);
            Assert(wiki.IsBlocked, "explicit block takes priority over allow");
            vm.UnblockAll();
            var geo = vm.GeoService.LookupAsync("8.8.8.8").GetAwaiter().GetResult();
            Assert(geo is { CountryCode: "US" } && geo.Lat != 0, "local GeoIP country and coordinates");
            Assert(vm.GeoService.LookupAsync("192.168.1.1").Result == null, "private IP is not geolocated");
            var settingsView=new SettingsWindow{DataContext=vm};
            var settingsContent=(FrameworkElement)settingsView.Content;
            settingsContent.Measure(new Size(650,800));settingsContent.Arrange(new Rect(0,0,650,800));settingsContent.UpdateLayout();
            var dnsCheckbox=Visuals(settingsContent).OfType<System.Windows.Controls.CheckBox>().First();
            Assert(dnsCheckbox.IsChecked==true,"settings binding reads current DNS exception");
            dnsCheckbox.IsChecked=false;
            Assert(!vm.Settings.Settings.AllowDnsTraffic,"settings binding writes user preference");
            dnsCheckbox.IsChecked=true;
            wiki.Location = geo;
            foreach (var d in chrome.Destinations)
                d.Refresh();
            var flag = new EShield.Converters.CountryFlagConverter().Convert("US", typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture);
            Assert(flag is BitmapImage, "offline Windows flag image");
            var html = (string)typeof(MapWindow).GetMethod("BuildMapHtml", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            Assert(html.Contains("postMessage({type:'select',processId:c.processId,flowId:c.flowId})"), "map sends structured app and connection identity");
            var page = new EShield.Views.Pages.ConnectionsPage { DataContext = vm };
            var row = new System.Windows.Controls.Border { DataContext = chrome.Destinations[0] };
            int before = vm.BlockRules.Rules.Count;
            typeof(EShield.Views.Pages.ConnectionsPage).GetMethod("DestRow_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { row, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent } });
            Assert(vm.BlockRules.Rules.Count == before, "destination click never blocks traffic");
            chrome.AppIcon = IconService.GetIcon(Environment.ProcessPath!);
            vm.FocusConnection(chrome, wiki);
            page.DataContext = vm;
            page.Width = 800;
            page.Height = 900;
            page.Measure(new Size(800, 900));
            page.Arrange(new Rect(0, 0, 800, 900));
            page.UpdateLayout();
            var image = new RenderTargetBitmap(800, 900, 96, 96, PixelFormats.Pbgra32);
            image.Render(page);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var imagePath = Path.Combine(args.Length > 0 ? args[0] : dir, "connections-qa.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            using (var stream = File.Create(imagePath))
                encoder.Save(stream);
            Console.WriteLine($"PASS: {_passed} regression checks. Render: {imagePath}");
            if (args.Contains("--map"))
                RunMapCheck(vm, args[0]);
        }
        finally { vm.Shutdown(); }
    }
    private static void Assert(bool condition, string test)
    {
        if (!condition)
            throw new Exception("FAIL: " + test);
        _passed++;
        Console.WriteLine("PASS: " + test);
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;}
    private static void RunPersistentCheck()
    {
        const string prefix = "[EShield-Test] Rule ";
        var path = Environment.ProcessPath!;
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        try
        {
            using (var firewall = new FirewallService(true, prefix))
            {
                Assert(firewall.IsAvailable, "elevated WFP service available");
                var process = new NetworkProcess { Name = "isolated test", ExecutablePath = path };
                var rule = new BlockRule { ExecutablePath = path, Domain = "127.0.0.1", RuleType = RuleType.Both };
                Assert(firewall.Configure(ConnectionMode.AllowAll, new[] { rule }, Array.Empty<AllowRule>(), new[] { process }, new()), "scoped policy applies");
                Assert(firewall.LastWarning == null, "persistent Windows Firewall write succeeds");
            }
            int count = 0;
            foreach (dynamic r in policy.Rules)
            {
                string name = r.Name;
                if (!name.StartsWith(prefix))
                    continue;
                count++;
                Assert(((string)r.ApplicationName).Equals(path, StringComparison.OrdinalIgnoreCase) && ((string)r.RemoteAddresses).StartsWith("127.0.0.1"), "saved rule has exact app and remote address");
            }
            Assert(count == 2, "inbound and outbound explicit blocks survive session close");
            using (var firewall = new FirewallService(true, prefix))
                Assert(firewall.Configure(ConnectionMode.AllowAll, Array.Empty<BlockRule>(), Array.Empty<AllowRule>(), Array.Empty<NetworkProcess>(), new()), "empty startup policy removes stale saved rules");
            count = 0;
            foreach (dynamic r in policy.Rules)
                if (((string)r.Name).StartsWith(prefix))
                    count++;
            Assert(count == 0, "unblock removes only isolated test rules");
            Console.WriteLine("ALL PERSISTENCE CHECKS PASSED");
        }
        finally { var names = new List<string>(); foreach (dynamic r in policy.Rules) if (((string)r.Name).StartsWith(prefix)) names.Add((string)r.Name); foreach (var name in names) policy.Rules.Remove(name); }
    }
    private static void RunMapCheck(MainViewModel vm, string directory)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        Exception? failure = null;
        var owner = new Window { Title = "EShield integration test", Width = 700, Height = 750, Left = 20, Top = 40, Background = (Brush)Application.Current.Resources["BgBrush"], Content = new EShield.Views.Pages.ConnectionsPage { DataContext = vm } };
        var map = new MapWindow(Path.Combine(directory, "map-profile")) { DataContext = vm };
        owner.Loaded += async (_, _) =>
        {
            try
            {
                foreach (var p in vm.Processes)
                    foreach (var f in p.Flows)
                        f.Location = await vm.GeoService.LookupAsync(f.RemoteIp);
                map.Owner = owner;
                map.Show();
                var web = (Microsoft.Web.WebView2.Wpf.WebView2)map.FindName("MapView");
                for (int i = 0; i < 80; i++)
                {
                    await Task.Delay(250);
                    if (web.CoreWebView2 != null && await web.CoreWebView2.ExecuteScriptAsync("typeof window.setConnections === 'function'") == "true")
                        break;
                }
                Assert(web.CoreWebView2 != null && await web.CoreWebView2.ExecuteScriptAsync("typeof window.setConnections === 'function'") == "true", "production map initializes with bundled Leaflet");
                await Task.Delay(1500);
                vm.SearchText = "hidden-by-search";
                vm.FilterMode = "Blocked";
                vm.SelectedFlow = null;
                await web.CoreWebView2!.ExecuteScriptAsync("var firstPin; pins.eachLayer(function(m){if(!firstPin)firstPin=m;}); firstPin.fire('click');");
                await Task.Delay(300);
                Assert(vm.SelectedFlow != null && vm.SelectedProcess?.IsExpanded == true && vm.SearchText == "" && vm.FilterMode == "All", "live map pin opens matching app and exact connection");
                await web.CoreWebView2.ExecuteScriptAsync("var button=document.querySelector('.leaflet-popup-content button'); if(button)button.click();");
                using (var output = File.Create(Path.Combine(directory, "map-qa.png")))
                    await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, output);
                Console.WriteLine("PASS: production WebView2 map interaction and screenshot");
            }
            catch (Exception e) { failure = e; }
            finally { map.Close(); owner.Close(); frame.Continue = false; }
        };
        owner.Show();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        if (failure != null)
            throw failure;
    }
}

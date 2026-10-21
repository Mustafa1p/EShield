using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using EShield.Models;
using EShield.Services;
using EShield.ViewModels;
using Microsoft.Web.WebView2.Core;
namespace EShield.Views;
public partial class MapWindow : Window
{
    private readonly string MapDir;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _ready, _dirty = true, _focusPending;
    private string _lastData = "";
    private NetworkProcess? _focusProcess;
    private NetworkFlow? _focusFlow;
    private string? _focusDomain;
    public MapWindow(string? cacheDirectory = null)
    {
        MapDir = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EShield", "map-v2");
        InitializeComponent();
        Closed += OnClosed;
        _timer.Tick += async (_, _) => { if (_dirty) { _dirty = false; await UpdateMapAsync(); } };
    }
    private void PositionBesideOwner()
    {
        if (Owner == null)
            return;
        var work = SystemParameters.WorkArea;
        Left = Math.Min(Owner.Left + Owner.ActualWidth, work.Right - Width);
        Top = Owner.Top;
        Height = Owner.ActualHeight;
    }
    private void OwnerMoved(object? s, EventArgs e) => PositionBesideOwner();
    private void OwnerSized(object s, SizeChangedEventArgs e) => PositionBesideOwner();
    private async void Window_Loaded(object s, RoutedEventArgs e)
    {
        PositionBesideOwner();
        if (Owner != null)
        {
            Owner.LocationChanged += OwnerMoved;
            Owner.SizeChanged += OwnerSized;
        }
        if (DataContext is MainViewModel vm)
            vm.ConnectionsChanged += ConnectionsChanged;
        try
        {
            Directory.CreateDirectory(MapDir);
            foreach (var file in new[] { "leaflet.js", "leaflet.css" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Resources", "Map", file), Path.Combine(MapDir, file), true);
            await File.WriteAllTextAsync(Path.Combine(MapDir, "map.html"), BuildMapHtml());
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(MapDir, "webview2"));
            await MapView.EnsureCoreWebView2Async(env);
            var core = MapView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.UserAgent += " EShield/0.1.0";
            core.SetVirtualHostNameToFolderMapping("eshield-map.internal", MapDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping("eshield-flags.internal", Path.Combine(AppContext.BaseDirectory, "Resources", "Flags"), CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += async (_, a) =>
            {
                if (!a.IsSuccess)
                {
                    SubtitleText.Text = "Map failed to load";
                    return;
                }
                _ready = await core.ExecuteScriptAsync("typeof window.setConnections === 'function'") == "true";
                if (_ready)
                {
                    _timer.Start();
                    await UpdateMapAsync();
                }
                else
                    SubtitleText.Text = "Map files unavailable";
            };
            core.Navigate("https://eshield-map.internal/map.html");
        }
        catch (Exception ex) { SubtitleText.Text = "Map error: " + ex.Message; }
    }
    private void ConnectionsChanged() => _dirty = true;
    public void FocusTraffic(NetworkProcess? process, NetworkFlow? flow, string? domain = null)
    {
        _focusProcess = process;
        _focusFlow = flow;
        _focusDomain = domain;
        _focusPending = true;
        _lastData = "";
        _dirty = true;
        if (_ready)
            _ = UpdateMapAsync();
    }
    private async Task UpdateMapAsync()
    {
        if (!_ready || DataContext is not MainViewModel vm)
            return;
        var items = vm.Processes.Where(p => _focusProcess == null || ReferenceEquals(p, _focusProcess))
            .SelectMany(p => p.Flows.Where(f => (_focusFlow == null || ReferenceEquals(f, _focusFlow)) && (_focusDomain == null || RuleMatcher.Matches(null, _focusDomain, p, f))).Select(f => (p, f)))
            .Where(x => x.f.Location != null).Select(x => new
            {
                processId = x.p.Id.ToString(),
                flowId = x.f.Id.ToString(),
                app = x.p.Name,
                path = x.p.ExecutablePath,
                host = x.f.Hostname ?? x.f.RemoteIp,
                ip = x.f.RemoteIp,
                port = x.f.RemotePort,
                protocol = x.f.Protocol.ToString(),
                direction = x.f.Direction.ToString(),
                blocked = x.f.IsBlocked,
                country = x.f.Location!.Country,
                city = x.f.Location.City,
                region = x.f.Location.Region,
                code = x.f.Location.CountryCode,
                lat = x.f.Location.Lat,
                lon = x.f.Location.Lon
            }).ToArray();
        var json = JsonSerializer.Serialize(items);
        if (json != _lastData || _focusPending)
        {
            await MapView.CoreWebView2.ExecuteScriptAsync($"setConnections({json},{(_focusPending ? "true" : "false")});");
            _lastData = json;
            _focusPending = false;
        }
        SubtitleText.Text = items.Length == 0 ? "No public destinations with a known location" : $"{items.Length} connections · {items.Select(x => x.country).Distinct().Count()} countries · approximate locations";
        CountText.Text = _focusProcess?.Name ?? "All apps";
    }
    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            // Accept older string messages as well as native WebView2 object messages.
            if (root.ValueKind == JsonValueKind.String)
            {
                using var nested = JsonDocument.Parse(root.GetString()!);
                Select(nested.RootElement);
            }
            else
                Select(root);
            void Select(JsonElement message)
            {
                if (!message.TryGetProperty("processId", out var process) || !message.TryGetProperty("flowId", out var flow))
                    return;
                var p = vm.Processes.FirstOrDefault(p => p.Id.ToString() == process.GetString());
                var f = p?.Flows.FirstOrDefault(f => f.Id.ToString() == flow.GetString());
                vm.FocusConnection(p, f);
            }
        }
        catch (JsonException) { }
    }
    private async void Refresh_Click(object s, RoutedEventArgs e)
    {
        _focusProcess = null;
        _focusFlow = null;
        _focusDomain = null;
        _focusPending = true;
        _lastData = "";
        await UpdateMapAsync();
    }
    private void ClosePanel_Click(object s, RoutedEventArgs e) => Close();
    private void OnClosed(object? s, EventArgs e)
    {
        _timer.Stop();
        if (DataContext is MainViewModel vm)
            vm.ConnectionsChanged -= ConnectionsChanged;
        if (Owner != null)
        {
            Owner.LocationChanged -= OwnerMoved;
            Owner.SizeChanged -= OwnerSized;
        }
        MapView.Dispose();
    }
    private static string BuildMapHtml() => """
<!DOCTYPE html><html><head><meta charset="utf-8"><meta name="referrer" content="strict-origin-when-cross-origin">
<link rel="stylesheet" href="leaflet.css"><script src="leaflet.js"></script>
<style>
*{box-sizing:border-box}html,body,#map{width:100%;height:100%;margin:0;background:#0d1117;font-family:'Segoe UI',sans-serif}
.leaflet-tile-pane{filter:invert(1) hue-rotate(180deg) brightness(.7) saturate(.6)}
.leaflet-popup-content-wrapper,.leaflet-popup-tip{background:#161b22;color:#e6edf3;border:1px solid #30363d}
.leaflet-popup-content{margin:12px;min-width:220px;max-height:350px;overflow:auto}
.connection{border-bottom:1px solid #30363d;padding:7px 0}.connection:last-child{border:0}
button{display:block;text-align:left;border:0;background:transparent;color:#86efac;font-size:13px;font-weight:600;cursor:pointer;padding:0 0 4px;width:100%}
button:hover{text-decoration:underline}small{display:block;color:#a6b3c2;line-height:1.65;overflow-wrap:anywhere}
.blocked{color:#fb7777}.leaflet-control-zoom a{background:#161b22!important;color:#e6edf3!important}
.leaflet-control-attribution{background:rgba(13,17,23,.95)!important;color:#a5adb8!important;font-size:10px}.leaflet-control-attribution a{color:#b6c5d8!important}
#hint{position:absolute;z-index:500;top:12px;left:52px;right:12px;background:#161b22df;border-radius:6px;padding:8px 12px;color:#c5d0dd;font-size:11px;pointer-events:none}
</style></head><body><div id="map"></div><div id="hint">Select a pin or app name to open its connection in the list.</div><script>
var map=L.map('map',{center:[25,10],zoom:2,minZoom:1,maxZoom:12,worldCopyJump:true});
var tiles=L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:19,attribution:'&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors · <a href="https://db-ip.com">IP Geolocation by DB-IP</a>'}).addTo(map);
tiles.on('tileerror',function(){document.getElementById('hint').textContent='Map tiles unavailable. Check your connection; destination pins remain available.'});
var pins=L.layerGroup().addTo(map), first=true;
function select(c){if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage({type:'select',processId:c.processId,flowId:c.flowId});}
function popup(items){var root=document.createElement('div');items.forEach(function(c){var row=document.createElement('div');row.className='connection';var button=document.createElement('button');button.textContent=c.app+' · '+(c.blocked?'Blocked':'Allowed');if(c.blocked)button.className='blocked';button.onclick=function(){select(c)};row.appendChild(button);[c.host,c.ip+':'+c.port+' · '+c.protocol+' · '+c.direction,[c.city,c.region,c.country].filter(Boolean).filter(function(v,i,a){return a.indexOf(v)===i}).join(', ')].forEach(function(text){var line=document.createElement('small');line.textContent=text;row.appendChild(line)});root.appendChild(row)});return root;}
function setConnections(connections,fit){
 var open=map._popup&&map._popup._source&&map._popup._source._key;
 pins.clearLayers();var groups={};connections.forEach(function(c){var key=c.lat.toFixed(3)+','+c.lon.toFixed(3);(groups[key]||(groups[key]=[])).push(c)});
 Object.keys(groups).forEach(function(key){var items=groups[key],c=items[0],allBlocked=items.every(function(x){return x.blocked});var color=allBlocked?'#f87171':'#4ade80';
 var icon=L.divIcon({className:'',html:'<div style="width:14px;height:14px;border-radius:50%;background:'+color+';border:2px solid white;box-shadow:0 0 0 5px '+color+'44"></div>',iconSize:[14,14],iconAnchor:[7,7]});
 var marker=L.marker([c.lat,c.lon],{icon:icon}).bindPopup(popup(items),{maxWidth:340});marker._key=key;marker.on('click',function(){select(items[0])});pins.addLayer(marker);if(key===open)marker.openPopup();
 });
 if((fit||first)&&connections.length){map.fitBounds(L.latLngBounds(connections.map(function(c){return[c.lat,c.lon]})),{padding:[45,45],maxZoom:6});first=false;}
}
window.setConnections=setConnections;
</script></body></html>
""";
}

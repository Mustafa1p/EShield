using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EShield.Models;
using EShield.ViewModels;
namespace EShield.Views.Pages;
public partial class ConnectionsPage : UserControl
{
    private MainViewModel? _vm;
    public ConnectionsPage() => InitializeComponent();
    private void Page_Loaded(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (_vm != null)
                _vm.FocusRequested -= FocusRow;
            _vm = vm;
            vm.FocusRequested += FocusRow;
        }
    }
    private void Page_Unloaded(object s, RoutedEventArgs e)
    {
        if (_vm != null)
            _vm.FocusRequested -= FocusRow;
        _vm = null;
    }
    private void FocusRow(NetworkProcess proc, NetworkFlow? flow)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ProcessList.UpdateLayout();
            var target = Walk(ProcessList).OfType<FrameworkElement>().FirstOrDefault(x => flow != null && ReferenceEquals(x.DataContext, flow));
            target ??= Walk(ProcessList).OfType<FrameworkElement>().FirstOrDefault(x => ReferenceEquals(x.DataContext, proc));
            target?.BringIntoView();
        });
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Walk(child))
                yield return nested;
        }
    }
    private void AppRow_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { DataContext: NetworkProcess p })
        {
            p.IsExpanded = !p.IsExpanded;
            if (_vm != null)
                _vm.SelectedProcess = p;
        }
    }
    private void Sparkline_Click(object s, MouseButtonEventArgs e)
    {
        if (s is FrameworkElement fe && FindProcess(fe) is { } p)
        {
            var owner = Window.GetWindow(this);
            var popup = new BandwidthPopupWindow { Owner = owner };
            popup.Populate(p, owner!);
            popup.Show();
        }
        e.Handled = true;
    }
    private void DestRow_Click(object s, MouseButtonEventArgs e)
    {
        if (s is FrameworkElement { DataContext: DestinationGroup d } fe)
        {
            d.IsExpanded = !d.IsExpanded;
            if (_vm != null)
            {
                _vm.SelectedProcess = FindProcess(fe);
                _vm.SelectedFlow = d.Flows.FirstOrDefault();
            }
        }
        e.Handled = true;
    }
    private void FlowRow_Click(object s, MouseButtonEventArgs e)
    {
        if (s is FrameworkElement { DataContext: NetworkFlow f } fe && _vm != null)
        {
            _vm.SelectedProcess = FindProcess(fe);
            _vm.SelectedFlow = f;
            foreach (var p in _vm.Processes)
                foreach (var flow in p.Flows)
                    flow.IsSelected = ReferenceEquals(flow, f);
        }
        e.Handled = true;
    }
    private void BlockDestination_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { DataContext: DestinationGroup d } fe && FindProcess(fe) is { } p)
            _vm?.BlockDomain(d.Key, p);
        e.Handled = true;
    }
    private void ShowMap_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement fe && FindProcess(fe) is { } p)
            _vm?.ShowInMap(p);
        e.Handled = true;
    }
    private void Row_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        if (s is not FrameworkElement fe || _vm == null)
            return;
        var p = FindProcess(fe);
        if (p == null)
            return;
        var menu = fe.ContextMenu ?? new ContextMenu();
        menu.Items.Clear();
        fe.ContextMenu = menu;
        void Add(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        if (fe.DataContext is NetworkProcess)
        {
            Add("Show in Map", () => _vm.ShowInMap(p));
            Add("Block this app", () => _vm.BlockProcess(p));
            Add("Unblock this app", () => _vm.UnblockProcess(p));
            Add("Add app to Allow List", () => _vm.AllowProcess(p));
            Add("Copy executable path", () => Clipboard.SetText(p.ExecutablePath));
        }
        else
        {
            var flow = fe.DataContext as NetworkFlow;
            var dest = fe.DataContext as DestinationGroup;
            var target = flow?.RemoteIp ?? (dest?.IsIpGroup == false ? dest.Key : null);
            Add("Show in Map", () => _vm.ShowInMap(p, flow, dest?.IsIpGroup == false ? dest.Key : null));
            if (target != null)
            {
                Add("Block this destination for " + p.Name, () => _vm.BlockDomain(target, p));
                Add("Remove this destination rule", () => _vm.UnblockDestination(target, p));
                Add("Allow this destination for " + p.Name, () => _vm.AllowDestination(target, p));
                Add("Copy destination", () => Clipboard.SetText(target));
            }
        }
        e.Handled = true;
        menu.IsOpen = true;
    }
    private static NetworkProcess? FindProcess(DependencyObject node)
    {
        for (DependencyObject? p = node; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is FrameworkElement { DataContext: NetworkProcess proc })
                return proc;
        return null;
    }
}

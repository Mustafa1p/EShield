using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EShield.Models;
using EShield.ViewModels;

namespace EShield.Views.Pages;

public partial class ApprovalPanelView : UserControl
{
    public ApprovalPanelView() => InitializeComponent();

    private void AppHeader_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is ApprovalRequest req)
        {
            req.IsExpanded = !req.IsExpanded;
            // Also select this app
            if (DataContext is MainViewModel vm)
                vm.SelectedApproval = req;
        }
    }

    private static readonly Brush _hoverBg = new SolidColorBrush(Color.FromRgb(0x5C, 0x2D, 0x00));

    private void ConnRow_MouseEnter(object s, MouseEventArgs e)
    {
        if (s is Border b)
        {
            b.Background = _hoverBg;
            if (b.DataContext is PendingConnection pc)
                pc.IsHovered = true;
        }
    }

    private void ConnRow_MouseLeave(object s, MouseEventArgs e)
    {
        if (s is Border b)
        {
            b.Background = Brushes.Transparent;
            if (b.DataContext is PendingConnection pc)
                pc.IsHovered = false;
        }
    }

    private void ConnRow_Click(object s, MouseButtonEventArgs e)
    {
        // Select the parent ApprovalRequest when a connection row is clicked
        if (s is FrameworkElement fe)
        {
            var parent = FindParentRequest(fe);
            if (parent != null && DataContext is MainViewModel vm)
                vm.SelectedApproval = parent;
        }
        e.Handled = true;
    }

    private static ApprovalRequest? FindParentRequest(DependencyObject child)
    {
        var p = VisualTreeHelper.GetParent(child);
        while (p != null)
        {
            if (p is FrameworkElement fe && fe.DataContext is ApprovalRequest req)
                return req;
            p = VisualTreeHelper.GetParent(p);
        }
        return null;
    }
}

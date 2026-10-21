using System.Windows;
using System.Windows.Input;
using EShield.ViewModels;

namespace EShield.Views;

/// <summary>
/// R23 — Non-modal approval window (NSPanel equivalent).
/// Shown with Show() not ShowDialog() so the main window stays interactive.
/// Topmost=True, ShowInTaskbar=False matches NSPanel floating behavior.
/// R10 — Escape dismisses without deciding (not the same as Block).
/// </summary>
public partial class ApprovalWindow : Window
{
    public ApprovalWindow() => InitializeComponent();

    // R10 — Escape = dismiss without deciding (cancel action)
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        if (DataContext is not MainViewModel vm)
            return;
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Enter && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            vm.ApproveAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && mods is ModifierKeys.Control or ModifierKeys.None)
        {
            vm.ApproveSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.B && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            vm.BlockSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.A && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            vm.BlockAll();
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Don't destroy — just hide (re-show when new approvals arrive)
        e.Cancel = true;
        Hide();
    }
}

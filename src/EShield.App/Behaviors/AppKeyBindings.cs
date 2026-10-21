using System.Windows;
using System.Windows.Input;

namespace EShield.Behaviors;

/// <summary>
/// R09 / R10 — wires Return = default action, Escape = cancel for any dialog-like window.
/// R07 — Ctrl+Q or Alt+F4 quits (Alt+F4 is native; we add Ctrl+Q).
/// R08 — Ctrl+, navigates to Settings.
/// R12 — Delete key removes selected rule in list context.
/// R20 — Ctrl+F focuses the search field.
/// </summary>
public static class AppKeyBindings
{
    // R08: Ctrl+, → Settings  (mirrors macOS ⌘,)
    public static readonly RoutedUICommand NavigateSettings =
        new("Settings", "NavigateSettings", typeof(AppKeyBindings),
            new InputGestureCollection { new KeyGesture(Key.OemComma, ModifierKeys.Control) });

    // R20: Ctrl+F → focus search field
    public static readonly RoutedUICommand FocusSearch =
        new("Focus Search", "FocusSearch", typeof(AppKeyBindings),
            new InputGestureCollection { new KeyGesture(Key.F, ModifierKeys.Control) });

    // R12: Delete → remove selected rule in list
    public static readonly RoutedUICommand DeleteSelectedRule =
        new("Delete Rule", "DeleteSelectedRule", typeof(AppKeyBindings),
            new InputGestureCollection { new KeyGesture(Key.Delete) });

    // R29: Quit & Switch to Allow All
    public static readonly RoutedUICommand QuitSwitchAllowAll =
        new("Quit & Switch to Allow All", "QuitSwitchAllowAll", typeof(AppKeyBindings));
}

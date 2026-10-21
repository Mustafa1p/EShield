using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EShield.Models;

namespace EShield.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is Visibility.Collapsed;
}

public class BlockedToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                  : new SolidColorBrush(Color.FromRgb(34, 197, 94));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => false;
}

public class BlockedToBadgeConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? "Blocked" : "Active";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => false;
}

public class ConnectionModeToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is ConnectionMode m ? m switch
        {
            ConnectionMode.AllowAll => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            ConnectionMode.RequireApproval => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            ConnectionMode.BlockAll => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _ => Brushes.Gray
        } : Brushes.Gray;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => ConnectionMode.AllowAll;
}

public class ConnectionModeToStringConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is ConnectionMode m ? m switch
        {
            ConnectionMode.AllowAll => "Allow All",
            ConnectionMode.RequireApproval => "Require Approval",
            ConnectionMode.BlockAll => "Block All",
            _ => "Unknown"
        } : "Unknown";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => ConnectionMode.AllowAll;
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => 0;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}

/// <summary>Converts IsExpanded bool to "> " or "∨ " arrow glyph.</summary>
public class ExpandArrowConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? "∨" : ">";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => false;
}

/// <summary>On hover (true) returns White foreground; otherwise returns TextDimBrush.</summary>
public class DestinationForegroundConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is true ? Brushes.White : new SolidColorBrush(Color.FromRgb(221, 221, 221));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => false;
}

public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v == null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => null!;
}

public class NameToColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.Color[] Colors = {
        System.Windows.Media.Color.FromRgb(32,120,255),
        System.Windows.Media.Color.FromRgb(16,185,129),
        System.Windows.Media.Color.FromRgb(239,68,68),
        System.Windows.Media.Color.FromRgb(245,158,11),
        System.Windows.Media.Color.FromRgb(139,92,246),
        System.Windows.Media.Color.FromRgb(236,72,153),
        System.Windows.Media.Color.FromRgb(14,165,233),
        System.Windows.Media.Color.FromRgb(249,115,22),
    };
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var name = v?.ToString() ?? "";
        int hash = string.IsNullOrEmpty(name) ? 0 : Math.Abs(name.GetHashCode());
        return Colors[hash % Colors.Length];
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        System.Windows.Media.Color.FromRgb(100, 100, 100);
}

public class InitialConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        var s = v?.ToString() ?? "?";
        return s.Length > 0 ? s[0].ToString().ToUpper() : "?";
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => "";
}

public class HighlightBrushConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, System.Globalization.CultureInfo c)
    {
        if (values.Length >= 2 && values[0] is string name && values[1] is string highlighted
            && !string.IsNullOrEmpty(highlighted)
            && name.Equals(highlighted, StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.FromArgb(40, 74, 222, 128));
        return System.Windows.Media.Brushes.Transparent;
    }
    public object[] ConvertBack(object v, Type[] t, object p, System.Globalization.CultureInfo c)
        => throw new NotImplementedException();
}

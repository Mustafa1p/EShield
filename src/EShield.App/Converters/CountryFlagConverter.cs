using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace EShield.Converters;
public sealed class CountryFlagConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Cache = new();
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var code = (value as string)?.ToLowerInvariant();
        if (code == null || code.Length != 2 || !code.All(char.IsAsciiLetter))
            return null;
        if (Cache.TryGetValue(code, out var img))
            return img;
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Flags", code + ".png");
        if (!File.Exists(path))
            return null;
        img = new(new Uri(path));
        img.Freeze();
        Cache[code] = img;
        return img;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

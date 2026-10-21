using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace EShield.Services;

/// <summary>Loads real executable icons; files without icons use the UI's letter fallback.</summary>
public static class IconService
{
    private static readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index,
        out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    public static ImageSource? GetIcon(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return null;
        if (_cache.TryGetValue(executablePath, out var cached))
            return cached;

        try
        {
            // .NET app hosts can expose a generic executable icon through the shell.
            // Use our packaged brand icon regardless of which host launched EShield.
            if (Path.GetFullPath(executablePath).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                var ownIcon = BitmapDecoder.Create(new Uri("pack://application:,,,/EShield;component/Resources/shield.ico"),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending(f => f.PixelWidth).First();
                ownIcon.Freeze();
                _cache[executablePath] = ownIcon;
                return ownIcon;
            }
            if (!File.Exists(executablePath))
                return null;

            // Extract embedded icons directly. SHGetFileInfo silently substitutes the
            // same generic Windows icon for executables that contain no icon resource.
            ExtractIconEx(executablePath, 0, out var large, out var small, 1);
            try
            {
                var handle = large != IntPtr.Zero ? large : small;
                if (handle == IntPtr.Zero)
                    return null;
                var img = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                img.Freeze();
                _cache[executablePath] = img;
                return img;
            }
            finally
            {
                if (large != IntPtr.Zero)
                    DestroyIcon(large);
                if (small != IntPtr.Zero && small != large)
                    DestroyIcon(small);
            }
        }
        catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Color FallbackColor(string name)
    {
        var colors = new[]
        {
            Color.FromRgb(32, 120, 255),
            Color.FromRgb(16, 185, 129),
            Color.FromRgb(239, 68, 68),
            Color.FromRgb(245, 158, 11),
            Color.FromRgb(139, 92, 246),
            Color.FromRgb(236, 72, 153),
            Color.FromRgb(14, 165, 233),
            Color.FromRgb(249, 115, 22),
        };
        uint hash = 2166136261;
        foreach (var character in name ?? "") hash = unchecked((hash ^ character) * 16777619);
        return colors[hash % colors.Length];
    }
}

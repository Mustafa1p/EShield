using System.Text.Json;
using System.IO;
using EShield.Models;
using Microsoft.Win32;

namespace EShield.Services;

public class SettingsService
{
    private readonly string _path;
    public SettingsService(string? directory = null)
    {
        _path = Path.Combine(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EShield"), "settings.json");
    }

    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { _settings = new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void SetLaunchAtStartup(bool enable)
    {
        _settings.LaunchAtStartup = enable;
        try
        {
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            using var rk = Registry.CurrentUser.OpenSubKey(key, true);
            if (rk == null)
                return;
            if (enable)
                rk.SetValue("EShield", $"\"{Environment.ProcessPath}\"");
            else
                rk.DeleteValue("EShield", false);
        }
        catch { }
        Save();
    }
}

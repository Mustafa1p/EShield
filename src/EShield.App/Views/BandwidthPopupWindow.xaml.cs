using System.Collections.Generic;
using System.Linq;
using System.Windows;
using EShield.Models;

namespace EShield.Views;

public partial class BandwidthPopupWindow : Window
{
    public BandwidthPopupWindow()
    {
        InitializeComponent();
    }

    public void Populate(NetworkProcess proc, Window owner)
    {
        AppNameText.Text = proc.Name;
        AppPathText.Text = proc.ExecutablePath;
        AppIconImg.Source = proc.AppIcon;

        var sent = proc.BytesSent;
        var recv = proc.BytesReceived;
        TotalSentText.Text = FormatBytes(sent);
        TotalRecvText.Text = FormatBytes(recv);

        // Live speed from last sample
        var samples = proc.BandwidthSamples;
        if (samples.Count > 0)
        {
            var last = samples[^1];
            SpeedUpText.Text = $"↑ {FormatBytes(last.sent)}/s";
            SpeedDownText.Text = $"↓ {FormatBytes(last.recv)}/s";
        }
        else
        {
            SpeedUpText.Text = "↑ 0 B/s";
            SpeedDownText.Text = "↓ 0 B/s";
        }

        BigSparkline.Data = proc.BandwidthSamples;

        // Top destinations
        var tops = proc.Destinations
            .OrderByDescending(d => d.Flows.Sum(f => f.BytesSent + f.BytesReceived))
            .Take(5)
            .Select(d => new KeyValuePair<string, string>(
                d.DisplayName,
                FormatBytes(d.Flows.Sum(f => f.BytesSent + f.BytesReceived))))
            .ToList();
        DestList.ItemsSource = tops;

        // Position to the right of the owner window, near where the user clicked
        if (owner != null)
        {
            Left = owner.Left + owner.ActualWidth + 8;
            Top = owner.Top + (owner.ActualHeight - Height) / 2;
        }
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();

    private static string FormatBytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1_048_576 => $"{b / 1024.0:F1} KB",
        < 1_073_741_824 => $"{b / 1_048_576.0:F2} MB",
        _ => $"{b / 1_073_741_824.0:F2} GB"
    };
}

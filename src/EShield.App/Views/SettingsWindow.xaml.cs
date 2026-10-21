using System.Windows;
using EShield.Models;
using EShield.ViewModels;

namespace EShield.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();
    private MainViewModel VM => (MainViewModel)DataContext;

    private void SetAllowAll_Click(object s, RoutedEventArgs e) => VM.ConnectionMode = ConnectionMode.AllowAll;
    private void SetRequireApproval_Click(object s, RoutedEventArgs e) => VM.ConnectionMode = ConnectionMode.RequireApproval;
    private void SetBlockAll_Click(object s, RoutedEventArgs e) => VM.ConnectionMode = ConnectionMode.BlockAll;
    private void Save_Click(object s, RoutedEventArgs e)
    {
        VM.Settings.Save();
        VM.RefreshPolicy();
    }
    private void Startup_Click(object s, RoutedEventArgs e) =>
        VM.Settings.SetLaunchAtStartup(VM.Settings.Settings.LaunchAtStartup);

}

using System.Windows;
using EShield.ViewModels;
using Microsoft.Win32;
namespace EShield.Views;
public partial class AllowListWindow : Window
{
    public AllowListWindow() => InitializeComponent();
    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose an app to allow", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && DataContext is AllowListViewModel vm)
        {
            vm.NewRuleInput = dialog.FileName;
            vm.AddDomainRuleCommand.Execute(null);
        }
    }
}

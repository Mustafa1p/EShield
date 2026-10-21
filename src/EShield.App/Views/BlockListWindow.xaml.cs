using System.Windows;
using EShield.ViewModels;
using Microsoft.Win32;
namespace EShield.Views;
public partial class BlockListWindow : Window
{
    public BlockListWindow() => InitializeComponent();
    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose an app to block", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && DataContext is BlockListViewModel vm)
        {
            vm.NewRuleInput = dialog.FileName;
            vm.AddDomainRuleCommand.Execute(null);
        }
    }
}

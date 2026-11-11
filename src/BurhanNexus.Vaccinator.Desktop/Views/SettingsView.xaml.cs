using System.Windows;
using System.Windows.Controls;
using BurhanNexus.Vaccinator.Desktop.ViewModels;

namespace BurhanNexus.Vaccinator.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnPassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) { vm.OldPassword = OldPwd.Password; vm.NewPassword = NewPwd.Password; }
    }
}

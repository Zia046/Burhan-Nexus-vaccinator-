using System.Windows;
using System.Windows.Controls;
using BurhanNexus.Vaccinator.Desktop.ViewModels;

namespace BurhanNexus.Vaccinator.Desktop.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private void OnPassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm) { vm.Password = Pwd.Password; vm.Confirm = Pwd2.Password; }
    }
}

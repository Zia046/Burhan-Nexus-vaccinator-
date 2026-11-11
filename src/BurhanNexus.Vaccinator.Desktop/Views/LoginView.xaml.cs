using System.Windows;
using System.Windows.Controls;
using BurhanNexus.Vaccinator.Desktop.ViewModels;

namespace BurhanNexus.Vaccinator.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += (_, _) => User.Focus();
    }

    private void OnPassword(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = Pwd.Password;
    }
}

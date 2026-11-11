using System.Windows.Controls;
using System.Windows.Input;
using BurhanNexus.Vaccinator.Desktop.ViewModels;

namespace BurhanNexus.Vaccinator.Desktop.Views;

public partial class PatientSearchView : UserControl
{
    public PatientSearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => Box.Focus();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PatientSearchViewModel vm) Ui.Run(vm.Open);
    }
}

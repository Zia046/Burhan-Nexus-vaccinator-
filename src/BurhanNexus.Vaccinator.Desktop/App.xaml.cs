using System.IO;
using System.Windows;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Infrastructure;
using BurhanNexus.Vaccinator.Desktop.ViewModels;

namespace BurhanNexus.Vaccinator.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Pakistani date style (day/month/year) in every date picker, independent of the Windows region setting.
        var culture = new System.Globalization.CultureInfo("en-GB");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        DispatcherUnhandledException += (_, args) =>
        {
            Ui.Log(args.Exception);
            MessageBox.Show("Something went wrong. The problem was written to the log file.\n\n" + args.Exception.Message, Ui.AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            try { if (BackupService.ApplyPendingRestore()) MessageBox.Show("The backup was restored.", Ui.AppName); } catch (Exception ex) { Ui.Log(ex); }
            DbFactory.Initialize();
            try { BackupService.AutomaticDailyBackup(); } catch (Exception ex) { Ui.Log(ex); }
            SettingsService.Load();

            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            MainWindow = window;
            window.Show();
            main.Start();
        }
        catch (Exception ex)
        {
            var path = Path.Combine(Path.GetTempPath(), "BurhanNexusVaccinator-startup-error.txt");
            try { File.WriteAllText(path, ex.ToString()); } catch { }
            MessageBox.Show("The application could not start.\n\n" + ex.Message + "\n\nDetails: " + path, Ui.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

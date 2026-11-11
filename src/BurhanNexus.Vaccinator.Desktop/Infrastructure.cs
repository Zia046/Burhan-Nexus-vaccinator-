using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.Win32;

namespace BurhanNexus.Vaccinator.Desktop
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>A page shown in the main working area. Title appears in the top toolbar.</summary>
    public abstract class PageViewModel : ViewModelBase
    {
        public abstract string Title { get; }
        public virtual void Refresh() { }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _run;
        private readonly Func<bool>? _can;
        public RelayCommand(Action run, Func<bool>? can = null) { _run = _ => run(); _can = can; }
        public RelayCommand(Action<object?> run) => _run = run;
        public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
        public bool CanExecute(object? parameter) => _can?.Invoke() ?? true;
        public void Execute(object? parameter) => Ui.Run(() => _run(parameter));
    }

    public static class Ui
    {
        public const string AppName = "Burhan Nexus Vaccinator";

        /// <summary>Runs an action and shows friendly messages for expected problems.</summary>
        public static bool Run(Action action)
        {
            try
            {
                using (new WaitCursor()) action();
                return true;
            }
            catch (UserException ex) { Warn(ex.Message); }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nThe details were written to the log file.", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }

        public static void Info(string message) => MessageBox.Show(message, AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        public static void Warn(string message) => MessageBox.Show(message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        public static bool Confirm(string message) => MessageBox.Show(message, AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        public static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(AppPaths.LogFolder, $"log-{DateTime.Now:yyyyMMdd}.txt"), $"[{DateTime.Now:HH:mm:ss}] {ex}\n\n"); } catch { }
        }

        public static DateOnly? ToDateOnly(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);
        public static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

        public static int? ParseInt(string? s) => int.TryParse(s?.Trim(), out var v) ? v : null;
        public static decimal? ParseDecimal(string? s) => decimal.TryParse(s?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

        public static string? SaveExcelDialog(string name)
        {
            var dlg = new SaveFileDialog { Filter = "Excel file (*.xlsx)|*.xlsx", FileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx".Replace(' ', '_') };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public static BitmapImage? Image(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        private sealed class WaitCursor : IDisposable
        {
            private readonly Cursor? _old;
            public WaitCursor() { _old = Mouse.OverrideCursor; Mouse.OverrideCursor = Cursors.Wait; }
            public void Dispose() => Mouse.OverrideCursor = _old;
        }
    }

    /// <summary>Opens a generated PDF in the PDF viewer (print preview) or sends it directly to the printer (Settings → Printer).</summary>
    public static class Printer
    {
        public static void Show(byte[] pdf, string fileName, int? patientId, string documentName)
        {
            var path = Path.Combine(AppPaths.PrintFolder, $"{fileName}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf".Replace(' ', '_').Replace('/', '-'));
            File.WriteAllBytes(path, pdf);
            var direct = SettingsService.Load().PrintDirectly;
            try
            {
                var info = new ProcessStartInfo(path) { UseShellExecute = true };
                if (direct) info.Verb = "print";
                Process.Start(info);
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { Ui.Warn("No PDF viewer is installed. The document was saved here:\n" + path); }
            }
            try { PatientService.LogPrint(patientId, documentName); } catch { }
        }
    }

    /// <summary>Small dialogs built in code: pick a date, type a reason.</summary>
    public static class Dialogs
    {
        public static DateOnly? AskDate(string title, string prompt, DateOnly initial)
        {
            var picker = new DatePicker { SelectedDate = initial.ToDateTime(TimeOnly.MinValue), Margin = new Thickness(0, 8, 0, 12) };
            return Show(title, prompt, picker) ? Ui.ToDateOnly(picker.SelectedDate) : null;
        }

        public static string? AskText(string title, string prompt, string initial = "")
        {
            var box = new TextBox { Text = initial, MinWidth = 320, Margin = new Thickness(0, 8, 0, 12) };
            return Show(title, prompt, box) ? box.Text : null;
        }

        private static bool Show(string title, string prompt, FrameworkElement input)
        {
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Background = System.Windows.Media.Brushes.SlateGray };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
            panel.Children.Add(input);
            panel.Children.Add(buttons);
            var w = new Window
            {
                Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = System.Windows.Application.Current?.MainWindow, ShowInTaskbar = false
            };
            ok.Click += (_, _) => w.DialogResult = true;
            input.Loaded += (_, _) => input.Focus();
            return w.ShowDialog() == true;
        }
    }
}

namespace BurhanNexus.Vaccinator.Desktop.Views
{
    public sealed class InverseBoolToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility v && v != Visibility.Visible;
    }

    public sealed class NullToCollapsed : IValueConverter
    {
        public static readonly NullToCollapsed Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is null || (value is string s && string.IsNullOrWhiteSpace(s)) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

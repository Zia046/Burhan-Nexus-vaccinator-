using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BurhanNexus.Vaccinator.Application;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

/// <summary>Decides the screen: first-run setup, login, or the application shell.</summary>
public sealed class MainViewModel : ViewModelBase
{
    private ViewModelBase? _current;
    public ViewModelBase? Current { get => _current; private set => Set(ref _current, value); }

    public void Start()
    {
        if (UserService.AnyUser()) ShowLogin(); else Current = new SetupViewModel(this);
    }

    public void ShowLogin() => Current = new LoginViewModel(this);
    public void ShowShell() => Current = new ShellViewModel(this);
}

public sealed class SetupViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    public SetupViewModel(MainViewModel main)
    {
        _main = main;
        SaveCommand = new RelayCommand(Save);
    }

    public string CenterName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
    public string Confirm { get; set; } = "";
    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        if (Password != Confirm) throw new UserException("The two passwords are not the same.");
        UserService.FirstSetup(CenterName, FullName, Username, Password);
        Ui.Info("Setup complete. Sign in with the administrator account.");
        _main.ShowLogin();
    }
}

public sealed class LoginViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private string _error = "";
    public LoginViewModel(MainViewModel main)
    {
        _main = main;
        CenterName = SettingsService.Load().CenterName;
        LoginCommand = new RelayCommand(Login);
    }

    public string CenterName { get; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Error { get => _error; set => Set(ref _error, value); }
    public RelayCommand LoginCommand { get; }

    private void Login()
    {
        try
        {
            UserService.Login(Username, Password);
            _main.ShowShell();
        }
        catch (UserException ex) { Error = ex.Message; }
    }
}

/// <summary>The application frame: top toolbar, working area and status bar. No side menu: navigation starts from the Home screen.</summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly Stack<PageViewModel> _back = new();
    private readonly DispatcherTimer _timer;
    private PageViewModel _page = null!;
    private string _now = "";
    private string _centerName = "";
    private BitmapImage? _centerLogo;

    public ShellViewModel(MainViewModel main)
    {
        _main = main;
        var u = Session.User!;
        UserLine = $"{u.FullName}  ·  {Permissions.RoleName(u.Role)}";
        HomeCommand = new RelayCommand(GoHome);
        DashboardCommand = new RelayCommand(() => Navigate(new DashboardViewModel(this)), () => Session.Can(Module.Dashboard));
        BackCommand = new RelayCommand(GoBack, () => _back.Count > 0);
        RefreshCommand = new RelayCommand(() => Page.Refresh());
        SearchCommand = new RelayCommand(() => Navigate(new PatientSearchViewModel(this)));
        NewPatientCommand = new RelayCommand(() => Navigate(new PatientEntryViewModel(this, null)), () => Session.Can(Module.Patients));
        SettingsCommand = new RelayCommand(() => Navigate(new SettingsViewModel(this)));
        LogoutCommand = new RelayCommand(Logout);
        ReloadCenter();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Now = $"{Fmt.D(DateTime.Now)}   {Fmt.T(TimeOnly.FromDateTime(DateTime.Now))}   {DateTime.Now:dddd}";
        _timer.Start();
        Now = $"{Fmt.D(DateTime.Now)}   {Fmt.T(TimeOnly.FromDateTime(DateTime.Now))}   {DateTime.Now:dddd}";

        GoHome();
    }

    public string UserLine { get; }
    public string Now { get => _now; private set => Set(ref _now, value); }
    public string CenterName { get => _centerName; private set => Set(ref _centerName, value); }
    public BitmapImage? CenterLogo { get => _centerLogo; private set => Set(ref _centerLogo, value); }
    public string DeveloperLine { get; private set; } = "";
    public string Version => "v" + Branding.Version;

    public PageViewModel Page { get => _page; private set { Set(ref _page, value); Raise(nameof(Title)); Raise(nameof(CanGoBack)); } }
    public string Title => Page?.Title ?? "";
    public bool CanGoBack => _back.Count > 0;

    public RelayCommand HomeCommand { get; }
    public RelayCommand DashboardCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand NewPatientCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand LogoutCommand { get; }

    public void ReloadCenter()
    {
        var c = SettingsService.Load();
        CenterName = c.CenterName;
        CenterLogo = Ui.Image(c.Logo);
        DeveloperLine = $"Developed by {c.DeveloperName}  |  Mobile: {c.DeveloperMobile}  |  Email: {c.DeveloperEmail}";
        Raise(nameof(DeveloperLine));
    }

    public void Navigate(PageViewModel page)
    {
        if (_page is not null) _back.Push(_page);
        Page = page;
    }

    /// <summary>Replaces the current page (for example after saving a form, open the patient profile).</summary>
    public void Replace(PageViewModel page) => Page = page;

    public void GoBack()
    {
        if (_back.Count == 0) return;
        var p = _back.Pop();
        Page = p;
        Ui.Run(p.Refresh);
    }

    public void GoHome()
    {
        _back.Clear();
        Page = new HomeViewModel(this);
    }

    private void Logout()
    {
        _timer.Stop();
        Session.SignOut();
        _main.ShowLogin();
    }
}

public sealed record HomeTile(string Title, string Subtitle, string Glyph, string Accent, RelayCommand Command, bool Allowed);

public sealed class HomeViewModel : PageViewModel
{
    public HomeViewModel(ShellViewModel shell)
    {
        HomeTile T(string title, string sub, string glyph, string color, Func<PageViewModel> page, Module module) =>
            new(title, sub, glyph, color, new RelayCommand(() => shell.Navigate(page())), Session.Can(module));

        Tiles = new ObservableCollection<HomeTile>(new[]
        {
            T("New Patient Entry", "Register a patient", "", "#159C91", () => new PatientEntryViewModel(shell, null), Module.Patients),
            T("Old Patient Search", "Name, ID, CNIC, mobile", "", "#123B50", () => new PatientSearchViewModel(shell), Module.Dashboard),
            T("Newborn Baby Registration", "EPI schedule from birth", "", "#0EA5E9", () => new NewbornViewModel(shell), Module.Newborn),
            T("Check Vaccine Schedule", "Calculate dose dates", "", "#6B91B5", () => new ScheduleCheckViewModel(shell), Module.Dashboard),
            T("Available Vaccines", "Vaccine database", "", "#0F766E", () => new VaccinesViewModel(shell), Module.Dashboard),
            T("Anti-Rabies Management", "Animal bite PEP", "", "#B91C1C", () => new RabiesViewModel(shell), Module.AntiRabies),
            T("Insulin Management", "Issue, stock, history", "", "#7C3AED", () => new InsulinViewModel(shell), Module.Insulin),
            T("Today's Vaccinations", "Due and given today", "", "#15803D", () => new TodayViewModel(shell), Module.Dashboard),
            T("Upcoming Vaccinations", "Reminders & appointments", "", "#0369A1", () => new AppointmentsViewModel(shell, AppointmentFilter.Next7Days), Module.Dashboard),
            T("Missed / Overdue Doses", "Patients who did not come", "", "#C2410C", () => new AppointmentsViewModel(shell, AppointmentFilter.Overdue), Module.Dashboard),
            T("Vaccination History", "All administered doses", "", "#475569", () => new HistoryViewModel(shell), Module.Dashboard),
            T("Doctor Directory", "Advising doctors", "", "#0891B2", () => new DoctorsViewModel(shell), Module.Doctors),
            T("Vaccine Inventory", "Batches, stock, cold chain", "", "#A16207", () => new InventoryViewModel(shell), Module.Inventory),
            T("Reports & Statistics", "Daily, monthly, stock", "", "#1D4ED8", () => new ReportsViewModel(shell), Module.Reports),
            T("Print Vaccination Card", "Cards and schedules", "", "#334155", () => new PrintCardViewModel(shell), Module.Dashboard),
            T("Settings", "Center, users, protocols, backup", "", "#64748B", () => new SettingsViewModel(shell), Module.Dashboard)
        });
        Welcome = $"Welcome, {Session.DisplayName}";
        try
        {
            var dueToday = ScheduleService.Appointments(AppointmentFilter.Today).Count;
            var overdue = ScheduleService.Appointments(AppointmentFilter.Overdue).Count;
            Summary = $"Today: {dueToday} dose(s) due  ·  {overdue} overdue";
        }
        catch { Summary = ""; }
    }

    public override string Title => "Home";
    public ObservableCollection<HomeTile> Tiles { get; }
    public string Welcome { get; }
    public string Summary { get; }
}

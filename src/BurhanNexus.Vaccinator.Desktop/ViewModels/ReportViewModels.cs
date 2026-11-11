using System.Collections.ObjectModel;
using System.IO;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using BurhanNexus.Vaccinator.Reporting;
using Microsoft.Win32;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

public sealed record StatCard(string Title, string Value, string Glyph, string Accent);

public sealed record Bar(string Label, int Value, double Height, string Color);

public sealed class Chart
{
    public Chart(string title, IEnumerable<ChartPoint> points, string color)
    {
        Title = title;
        var list = points.ToList();
        var max = Math.Max(1, list.Count == 0 ? 1 : list.Max(p => p.Value));
        Bars = list.Select(p => new Bar(p.Label, p.Value, 4 + 130.0 * p.Value / max, color)).ToList();
        Empty = list.Sum(p => p.Value) == 0;
    }
    public string Title { get; }
    public List<Bar> Bars { get; }
    public bool Empty { get; }
}

public sealed class DashboardViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private DashboardData _d = new();

    public DashboardViewModel(ShellViewModel shell)
    {
        _shell = shell;
        OpenAppointmentCommand = new RelayCommand(p => { if (p is AppointmentRow a) _shell.Navigate(new PatientProfileViewModel(_shell, a.PatientId)); });
        OverdueCommand = new RelayCommand(() => _shell.Navigate(new AppointmentsViewModel(_shell, AppointmentFilter.Overdue)));
        Refresh();
    }

    public override string Title => "Dashboard";
    public DashboardData D { get => _d; private set => Set(ref _d, value); }
    public List<StatCard> Cards { get; private set; } = new();
    public List<Chart> Charts { get; private set; } = new();
    public RelayCommand OpenAppointmentCommand { get; }
    public RelayCommand OverdueCommand { get; }
    public string Updated { get; private set; } = "";

    public override void Refresh()
    {
        D = DashboardService.Load();
        Cards = new List<StatCard>
        {
            new("Registered Patients", D.TotalPatients.ToString("N0"), "", "#123B50"),
            new("Newborn Babies", D.TotalNewborns.ToString("N0"), "", "#0EA5E9"),
            new("Total Vaccinations", D.TotalVaccinations.ToString("N0"), "", "#159C91"),
            new("Today's Vaccinations", D.TodaysVaccinations.ToString("N0"), "", "#15803D"),
            new("Upcoming Vaccinations", D.UpcomingVaccinations.ToString("N0"), "", "#0369A1"),
            new("Overdue Vaccinations", D.OverdueVaccinations.ToString("N0"), "", "#B91C1C"),
            new("Active Anti-Rabies", D.ActiveRabiesPatients.ToString("N0"), "", "#C2410C"),
            new("Completed Anti-Rabies", D.CompletedRabiesCourses.ToString("N0"), "", "#16A34A"),
            new("Insulin Vials Issued", D.InsulinVialsIssued.ToString("N0"), "", "#7C3AED"),
            new("Available Vaccine Stock", D.AvailableVaccineStock.ToString("N0") + " doses", "", "#A16207")
        };
        Charts = new List<Chart>
        {
            new("Daily vaccinations (last 14 days)", D.Daily, "#159C91"),
            new("Monthly vaccinations (last 12 months)", D.Monthly, "#123B50"),
            new("Vaccine-wise distribution (12 months)", D.ByVaccine, "#0EA5E9"),
            new("Age-wise vaccinations (12 months)", D.ByAge, "#7C3AED"),
            new("Anti-Rabies statistics", D.Rabies, "#B91C1C")
        };
        Updated = "Updated " + Fmt.DT(DateTime.Now);
        RaiseAll();
    }
}

public sealed class ReportsViewModel : PageViewModel
{
    public ReportsViewModel(ShellViewModel shell)
    {
        DailyCommand = new RelayCommand(() => Period(Ui.ToDateOnly(Day) ?? Fmt.Today, Ui.ToDateOnly(Day) ?? Fmt.Today, "Daily Vaccination Report"));
        MonthlyCommand = new RelayCommand(() =>
        {
            var start = new DateOnly(Month.Year, Month.Month, 1);
            Period(start, start.AddMonths(1).AddDays(-1), $"Monthly Vaccination Report – {start:MMMM yyyy}");
        });
        RangeCommand = new RelayCommand(() => Period(Ui.ToDateOnly(From) ?? Fmt.Today, Ui.ToDateOnly(To) ?? Fmt.Today, "Vaccination Report"));
        StockCommand = new RelayCommand(() => Printer.Show(Pdf.StockReport(SettingsService.Load(), InventoryService.Batches()), "Stock-report", null, "Vaccine Stock Report"));
        InsulinStockCommand = new RelayCommand(() => Printer.Show(Pdf.InsulinStockReport(SettingsService.Load(), InsulinService.Batches()), "Insulin-stock", null, "Insulin Stock Report"));
        ExcelCommand = new RelayCommand(Excel);
        AuditCommand = new RelayCommand(LoadAudit, () => Session.Can(Module.Settings));
        Months = Enumerable.Range(0, 24).Select(i => DateTime.Today.AddMonths(-i)).Select(d => new DateTime(d.Year, d.Month, 1)).ToList();
        Month = Months[0];
        Refresh();
    }

    public override string Title => "Reports & Statistics";
    public DateTime? Day { get; set; } = DateTime.Today;
    public List<DateTime> Months { get; }
    public DateTime Month { get; set; }
    public DateTime? From { get; set; } = DateTime.Today.AddDays(-7);
    public DateTime? To { get; set; } = DateTime.Today;
    public List<ChartPoint> Summary { get; private set; } = new();
    public ObservableCollection<AuditRow> Audit { get; } = new();
    public RelayCommand DailyCommand { get; }
    public RelayCommand MonthlyCommand { get; }
    public RelayCommand RangeCommand { get; }
    public RelayCommand StockCommand { get; }
    public RelayCommand InsulinStockCommand { get; }
    public RelayCommand ExcelCommand { get; }
    public RelayCommand AuditCommand { get; }

    public override void Refresh()
    {
        var r = ReportService.Period(new DateOnly(Month.Year, Month.Month, 1), Fmt.Today, "This month");
        Summary = new List<ChartPoint>
        {
            new("Vaccinations this month", r.Vaccinations.Count), new("New patients this month", r.NewPatients),
            new("New anti-rabies cases", r.NewRabiesCases), new("Doses wasted", r.Wastage), new("Insulin vials issued", r.Insulin.Sum(i => i.Vials))
        };
        Summary.AddRange(r.ByVaccine);
        Raise(nameof(Summary));
    }

    private void Period(DateOnly from, DateOnly to, string title)
    {
        var r = ReportService.Period(from, to, title);
        Printer.Show(Pdf.PeriodReport(SettingsService.Load(), r), title.Replace(' ', '-'), null, title);
    }

    private void Excel()
    {
        var path = Ui.SaveExcelDialog("Vaccinations");
        if (path is null) return;
        var rows = VaccinationService.History(Ui.ToDateOnly(From), Ui.ToDateOnly(To));
        Reporting.Excel.Export<VaccinationRow>(path, "Vaccinations", rows,
            ("Record", r => r.RecordNo), ("Date", r => r.DateText), ("Time", r => r.TimeText), ("Patient ID", r => r.PatientNo), ("Patient", r => r.PatientName),
            ("Vaccine", r => r.Vaccine), ("Dose", r => r.Dose), ("Route", r => r.Route), ("Batch", r => r.Batch), ("By", r => r.AdministeredBy));
        Ui.Info("Saved: " + path);
    }

    private void LoadAudit()
    {
        Audit.Clear();
        foreach (var a in ReportService.AuditTrail()) Audit.Add(a);
    }
}

public sealed class PrintCardViewModel : PageViewModel
{
    private PatientRow? _selected;
    public PrintCardViewModel(ShellViewModel shell)
    {
        SearchCommand = new RelayCommand(() => { Results.Clear(); foreach (var r in PatientService.Search(Text)) Results.Add(r); });
        CardCommand = new RelayCommand(() => Print(p => p.Patient.Newborn is not null ? Pdf.NewbornCard(SettingsService.Load(), p) : Pdf.VaccinationCard(SettingsService.Load(), p),
            "Vaccination Card"), () => Selected is not null);
        NewbornCommand = new RelayCommand(() => Print(p => Pdf.NewbornCard(SettingsService.Load(), p), "Newborn Immunization Card"), () => Selected is not null);
        ScheduleCommand = new RelayCommand(() => Print(p => Pdf.UpcomingSchedule(SettingsService.Load(), p), "Upcoming Dose Schedule"), () => Selected is not null);
        HistoryCommand = new RelayCommand(() => Print(p => Pdf.PatientHistory(SettingsService.Load(), p), "Patient Vaccination History"), () => Selected is not null);
        FormCommand = new RelayCommand(() => Print(p => Pdf.RegistrationForm(SettingsService.Load(), p), "Patient Registration Form"), () => Selected is not null);
        RabiesCommand = new RelayCommand(PrintRabies, () => Selected is not null);
        SearchCommand.Execute(null);
    }

    public override string Title => "Print Vaccination Card";
    public string Text { get; set; } = "";
    public ObservableCollection<PatientRow> Results { get; } = new();
    public PatientRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public RelayCommand SearchCommand { get; }
    public RelayCommand CardCommand { get; }
    public RelayCommand NewbornCommand { get; }
    public RelayCommand ScheduleCommand { get; }
    public RelayCommand HistoryCommand { get; }
    public RelayCommand FormCommand { get; }
    public RelayCommand RabiesCommand { get; }

    private void Print(Func<PatientProfile, byte[]> make, string name)
    {
        if (Selected is null) return;
        var p = PatientService.Profile(Selected.Id);
        Printer.Show(make(p), $"{name}-{p.Patient.PatientNo}".Replace(' ', '-'), p.Patient.Id, name);
    }

    private void PrintRabies()
    {
        if (Selected is null) return;
        var p = PatientService.Profile(Selected.Id);
        var c = p.RabiesCases.FirstOrDefault() ?? throw new UserException("This patient has no anti-rabies case.");
        Printer.Show(Pdf.AntiRabiesCard(SettingsService.Load(), p, RabiesService.Load(c.Id)), $"AntiRabies-{c.CaseNo}", p.Patient.Id, "Anti-Rabies Vaccination Card");
    }
}

// ============================================================================ Settings

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private CenterProfile _c;
    private UserRow? _user;
    private Vaccine? _vaccine;

    public SettingsViewModel(ShellViewModel shell, int tab = 0)
    {
        _shell = shell;
        _c = SettingsService.Load();
        CanAdmin = Session.Can(Module.Settings);
        CanVaccines = Session.Can(Module.VaccineDatabase);
        TabIndex = CanAdmin ? tab : 4;
        SaveCenterCommand = new RelayCommand(SaveCenter, () => CanAdmin);
        LogoCommand = new RelayCommand(PickLogo, () => CanAdmin);
        RemoveLogoCommand = new RelayCommand(() => { _c.Logo = null; Raise(nameof(Logo)); }, () => CanAdmin);
        NewUserCommand = new RelayCommand(() => { SelectedUser = null; UName = UFull = UPassword = ""; URole = UserRole.Vaccinator; UActive = true; RaiseAll(); });
        SaveUserCommand = new RelayCommand(SaveUser, () => CanAdmin);
        BackupCommand = new RelayCommand(Backup, () => CanAdmin);
        RestoreCommand = new RelayCommand(Restore, () => CanAdmin);
        OpenBackupFolderCommand = new RelayCommand(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.BackupFolder) { UseShellExecute = true }));
        ChangePasswordCommand = new RelayCommand(ChangePassword);
        SaveVaccineCommand = new RelayCommand(SaveVaccine, () => CanVaccines && Vaccine is not null);
        NewVaccineCommand = new RelayCommand(() => { Vaccine = new Vaccine { IsActive = true, Route = AdministrationRoute.IM }; }, () => CanVaccines);
        EditProtocolCommand = new RelayCommand(p => { if (p is VaccineProtocol vp) _shell.Navigate(new ProtocolEditorViewModel(_shell, vp.Id, vp.VaccineId)); });
        NewProtocolCommand = new RelayCommand(() => { if (Vaccine is { Id: > 0 }) _shell.Navigate(new ProtocolEditorViewModel(_shell, 0, Vaccine.Id)); }, () => CanVaccines && Vaccine is { Id: > 0 });
        ApproveCommand = new RelayCommand(p => { if (p is VaccineProtocol vp) { VaccineDatabaseService.Approve(vp.Id, !vp.ApprovedByCenter); Refresh(); } });
        Refresh();
    }

    public override string Title => "Settings";
    public int TabIndex { get; set; }
    public bool CanAdmin { get; }
    public bool CanVaccines { get; }

    // center
    public string CenterName { get => _c.CenterName; set => _c.CenterName = value; }
    public string? Address { get => _c.Address; set => _c.Address = value; }
    public string? Phone { get => _c.Phone; set => _c.Phone = value; }
    public string? Email { get => _c.Email; set => _c.Email = value; }
    public System.Windows.Media.Imaging.BitmapImage? Logo => Ui.Image(_c.Logo);
    public List<string> DateFormats { get; } = new() { "dd-MM-yyyy", "dd/MM/yyyy", "dd-MMM-yyyy", "yyyy-MM-dd" };
    public string DateFormat { get => _c.DateFormat; set => _c.DateFormat = value; }
    public bool Use24Hour { get => _c.Use24Hour; set => _c.Use24Hour = value; }
    public bool PrintDirectly { get => _c.PrintDirectly; set => _c.PrintDirectly = value; }
    public string DeveloperName { get => _c.DeveloperName; set => _c.DeveloperName = value; }
    public string DeveloperEmail { get => _c.DeveloperEmail; set => _c.DeveloperEmail = value; }
    public string DeveloperMobile { get => _c.DeveloperMobile; set => _c.DeveloperMobile = value; }

    // users
    public ObservableCollection<UserRow> Users { get; } = new();
    public UserRow? SelectedUser
    {
        get => _user;
        set
        {
            Set(ref _user, value);
            if (value is null) return;
            UName = value.Username; UFull = value.FullName; URole = value.Role; UActive = value.IsActive; UPassword = "";
            Raise(nameof(UName)); Raise(nameof(UFull)); Raise(nameof(URole)); Raise(nameof(UActive)); Raise(nameof(UPassword));
        }
    }
    public string UName { get; set; } = "";
    public string UFull { get; set; } = "";
    public UserRole URole { get; set; } = UserRole.Vaccinator;
    public bool UActive { get; set; } = true;
    public string UPassword { get; set; } = "";
    public string RolesHelp => "Administrator: everything.  Vaccinator: patients, vaccinations, anti-rabies, insulin issue, reminders, reports.  " +
                               "Receptionist: registration, search, reminders (reschedule / not arrived), printing.  Inventory Manager: vaccine and insulin stock, reports.";

    // backup
    public string BackupInfo => $"Database: {AppPaths.DatabaseFile}\nAutomatic backups (one per day, newest 30 kept): {AppPaths.BackupFolder}";

    // password
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";

    // vaccine database
    public ObservableCollection<Vaccine> VaccineList { get; } = new();
    public Vaccine? Vaccine { get => _vaccine; set { Set(ref _vaccine, value); Raise(nameof(VaccineProtocols)); } }
    public List<VaccineProtocol> VaccineProtocols => Vaccine?.Protocols.OrderBy(p => p.SortOrder).ToList() ?? new();

    // about
    public string AboutTitle => "BURHAN NEXUS VACCINATOR";
    public string AboutText => Branding.AboutText;
    public string AboutVersion => $"Version {Branding.Version}   ·   Released {Branding.ReleaseDate}";
    public string AboutDeveloper => $"Developed & Powered by {_c.DeveloperName}";
    public string AboutContact => $"Email: {_c.DeveloperEmail}     Mobile: {_c.DeveloperMobile}";
    public string AboutCopyright => $"Copyright © {DateTime.Today.Year} {_c.DeveloperName}. All rights reserved.";
    public string AboutSafety => "This software is a healthcare record-management and scheduling tool. It does not diagnose, prescribe or replace professional medical judgement. Vaccine schedules come from the configured protocols, which must be reviewed and approved by the center's medical officer.";

    public RelayCommand SaveCenterCommand { get; }
    public RelayCommand LogoCommand { get; }
    public RelayCommand RemoveLogoCommand { get; }
    public RelayCommand NewUserCommand { get; }
    public RelayCommand SaveUserCommand { get; }
    public RelayCommand BackupCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }
    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand SaveVaccineCommand { get; }
    public RelayCommand NewVaccineCommand { get; }
    public RelayCommand EditProtocolCommand { get; }
    public RelayCommand NewProtocolCommand { get; }
    public RelayCommand ApproveCommand { get; }

    public override void Refresh()
    {
        Users.Clear();
        if (CanAdmin) foreach (var u in UserService.List()) Users.Add(u);
        var id = Vaccine?.Id;
        VaccineList.Clear();
        foreach (var v in VaccineDatabaseService.Vaccines()) VaccineList.Add(v);
        Vaccine = VaccineList.FirstOrDefault(v => v.Id == id) ?? VaccineList.FirstOrDefault();
    }

    private void SaveCenter()
    {
        SettingsService.Save(_c);
        _shell.ReloadCenter();
        Ui.Info("Settings saved.");
    }

    private void PickLogo()
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() != true) return;
        var bytes = File.ReadAllBytes(dlg.FileName);
        if (bytes.Length > 2_000_000) throw new UserException("The logo file is too large (maximum 2 MB).");
        if (Ui.Image(bytes) is null) throw new UserException("This file is not a picture.");
        _c.Logo = bytes;
        Raise(nameof(Logo));
    }

    private void SaveUser()
    {
        UserService.Save(SelectedUser?.Id, UName, UFull, URole, UActive, string.IsNullOrEmpty(UPassword) ? null : UPassword);
        UPassword = "";
        Refresh();
        Raise(nameof(UPassword));
        Ui.Info("User saved.");
    }

    private void Backup()
    {
        var dlg = new SaveFileDialog { Filter = "Vaccinator backup (*.db)|*.db", FileName = $"BurhanNexusVaccinator-backup-{DateTime.Now:yyyyMMdd-HHmm}.db" };
        if (dlg.ShowDialog() != true) return;
        BackupService.Create(dlg.FileName);
        Ui.Info("Backup saved:\n" + dlg.FileName + "\n\nKeep a copy on a USB drive or another computer.");
    }

    private void Restore()
    {
        var dlg = new OpenFileDialog { Filter = "Vaccinator backup (*.db)|*.db" };
        if (dlg.ShowDialog() != true) return;
        if (!Ui.Confirm("Restoring replaces ALL current data with the backup. The current data is saved first in the backups folder.\n\nContinue?")) return;
        BackupService.PrepareRestore(dlg.FileName);
        Ui.Info("The backup will be restored when the program starts again. The program will now close.");
        System.Windows.Application.Current.Shutdown();
    }

    private void ChangePassword()
    {
        UserService.ChangeOwnPassword(OldPassword, NewPassword);
        OldPassword = NewPassword = "";
        RaiseAll();
        Ui.Info("Password changed.");
    }

    private void SaveVaccine()
    {
        if (Vaccine is null) return;
        VaccineDatabaseService.SaveVaccine(Vaccine);
        Refresh();
        Ui.Info("Vaccine saved.");
    }
}

/// <summary>Edits one protocol and its doses. Any change to dates/intervals resets the center approval.</summary>
public sealed class ProtocolEditorViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    public ProtocolEditorViewModel(ShellViewModel shell, int protocolId, int vaccineId)
    {
        _shell = shell;
        var p = protocolId == 0 ? null : VaccineDatabaseService.Protocol(protocolId);
        Protocol = p ?? new VaccineProtocol { VaccineId = vaccineId, Anchor = ScheduleAnchor.FirstDose, Kind = ProtocolKind.Routine, IsActive = true };
        foreach (var d in (p?.Doses ?? new List<ProtocolDose> { new() { DoseNumber = 1, Label = "Dose 1", OffsetUnit = IntervalUnit.Days } }).OrderBy(d => d.DoseNumber))
            Doses.Add(new DoseEdit(d));
        AddDoseCommand = new RelayCommand(() => Doses.Add(new DoseEdit(new ProtocolDose { DoseNumber = Doses.Count + 1, Label = $"Dose {Doses.Count + 1}", OffsetUnit = IntervalUnit.Days })));
        RemoveDoseCommand = new RelayCommand(() => { if (SelectedDose is not null) Doses.Remove(SelectedDose); });
        SaveCommand = new RelayCommand(Save, () => Session.Can(Module.VaccineDatabase));
        ReviewDays = Protocol.ReviewIfLateByDays?.ToString() ?? "";
    }

    public override string Title => Protocol.Id == 0 ? "New Protocol" : "Edit Protocol";
    public VaccineProtocol Protocol { get; }
    public string ReviewDays { get; set; }
    public ObservableCollection<DoseEdit> Doses { get; } = new();
    public DoseEdit? SelectedDose { get; set; }
    public RelayCommand AddDoseCommand { get; }
    public RelayCommand RemoveDoseCommand { get; }
    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        Protocol.ReviewIfLateByDays = Ui.ParseInt(ReviewDays);
        VaccineDatabaseService.SaveProtocol(Protocol, Doses.Select(d => d.ToDose()).ToList());
        Ui.Info("Protocol saved. It now needs approval by the center's medical officer before use (Settings → Vaccine Database → Approve). Courses already started are not changed.");
        _shell.GoBack();
    }
}

public sealed class DoseEdit
{
    public DoseEdit(ProtocolDose d)
    {
        DoseNumber = d.DoseNumber; Label = d.Label; OffsetValue = d.OffsetValue; OffsetUnit = d.OffsetUnit; MinInterval = d.MinIntervalValue?.ToString() ?? "";
        MinUnit = d.MinIntervalUnit ?? IntervalUnit.Days; Volume = d.DoseVolume ?? ""; Route = d.Route ?? AdministrationRoute.IM; Site = d.InjectionSite ?? ""; Booster = d.IsBooster;
    }
    public int DoseNumber { get; set; }
    public string Label { get; set; }
    public int OffsetValue { get; set; }
    public IntervalUnit OffsetUnit { get; set; }
    public string MinInterval { get; set; }
    public IntervalUnit MinUnit { get; set; }
    public string Volume { get; set; }
    public AdministrationRoute Route { get; set; }
    public string Site { get; set; }
    public bool Booster { get; set; }

    public ProtocolDose ToDose() => new()
    {
        DoseNumber = DoseNumber, Label = Label ?? "", OffsetValue = OffsetValue, OffsetUnit = OffsetUnit,
        MinIntervalValue = Ui.ParseInt(MinInterval), MinIntervalUnit = string.IsNullOrWhiteSpace(MinInterval) ? null : MinUnit,
        DoseVolume = Volume, Route = Route, InjectionSite = Site, IsBooster = Booster
    };
}

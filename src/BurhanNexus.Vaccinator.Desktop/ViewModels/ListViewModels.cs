using System.Collections.ObjectModel;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Reporting;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

public sealed class ScheduleCheckViewModel : PageViewModel
{
    private VaccineOption? _vaccine;
    private ProtocolItem? _protocol;
    private DateTime? _anchor = DateTime.Today;

    public ScheduleCheckViewModel(ShellViewModel shell)
    {
        Vaccines = VaccinationService.Vaccines();
        PrintCommand = new RelayCommand(Print, () => Protocol is not null);
        Vaccine = Vaccines.FirstOrDefault(v => v.Name == "Hepatitis B") ?? Vaccines.FirstOrDefault();
    }

    public override string Title => "Check Vaccine Schedule";
    public List<VaccineOption> Vaccines { get; }
    public VaccineOption? Vaccine
    {
        get => _vaccine;
        set
        {
            Set(ref _vaccine, value);
            Protocols = value is null ? new() : ScheduleService.Protocols(value.Id);
            Raise(nameof(Protocols));
            Protocol = Protocols.FirstOrDefault(p => p.Kind != ProtocolKind.ChildhoodEpi) ?? Protocols.FirstOrDefault();
        }
    }
    public List<ProtocolItem> Protocols { get; private set; } = new();
    public ProtocolItem? Protocol { get => _protocol; set { Set(ref _protocol, value); Calculate(); } }
    public DateTime? Anchor { get => _anchor; set { Set(ref _anchor, value); Calculate(); } }
    public string AnchorLabel => Protocol?.Anchor == ScheduleAnchor.DateOfBirth ? "Date of birth" : "Date of first dose";
    public ObservableCollection<PreviewDose> Doses { get; } = new();
    public string Summary { get; private set; } = "";
    public RelayCommand PrintCommand { get; }

    private void Calculate()
    {
        Doses.Clear();
        Raise(nameof(AnchorLabel));
        if (Protocol is null || Anchor is null) return;
        foreach (var d in ScheduleService.Preview(Protocol.Id, DateOnly.FromDateTime(Anchor.Value))) Doses.Add(d);
        var remaining = Doses.Count(d => d.Date >= Fmt.Today);
        var next = Doses.FirstOrDefault(d => d.Date >= Fmt.Today);
        Summary = $"Total doses: {Doses.Count}   ·   Doses on or after today: {remaining}" + (next is null ? "" : $"   ·   Next: {next.Label} on {next.DateText}");
        Raise(nameof(Summary));
    }

    private void Print()
    {
        if (Protocol is null) return;
        Printer.Show(Pdf.SchedulePreview(SettingsService.Load(), Protocol.VaccineName, Protocol.Name, null, Doses, Protocol.Reference), "Schedule", null, "Vaccine Schedule");
    }
}

public sealed class VaccinesViewModel : PageViewModel
{
    private Vaccine? _selected;
    public VaccinesViewModel(ShellViewModel shell)
    {
        _shell = shell;
        EditCommand = new RelayCommand(() => _shell.Navigate(new SettingsViewModel(_shell, 2)), () => Session.Can(Module.VaccineDatabase));
        Refresh();
    }

    private readonly ShellViewModel _shell;
    public override string Title => "Available Vaccines";
    public ObservableCollection<Vaccine> Items { get; } = new();
    public Vaccine? Selected { get => _selected; set { Set(ref _selected, value); Raise(nameof(SelectedProtocols)); } }
    public List<ProtocolView> SelectedProtocols => Selected?.Protocols.OrderBy(p => p.SortOrder).Select(p => new ProtocolView(p)).ToList() ?? new();
    public RelayCommand EditCommand { get; }

    public override void Refresh()
    {
        var id = Selected?.Id;
        Items.Clear();
        foreach (var v in VaccineDatabaseService.Vaccines().Where(v => v.IsActive)) Items.Add(v);
        Selected = Items.FirstOrDefault(v => v.Id == id) ?? Items.FirstOrDefault();
    }
}

public sealed class ProtocolView
{
    public ProtocolView(VaccineProtocol p)
    {
        Name = p.Name;
        Group = p.TargetGroup;
        Approved = p.ApprovedByCenter ? "Approved by center" + (p.ApprovedBy is null ? "" : $" ({p.ApprovedBy})") : "Not yet approved by center";
        Doses = string.Join("  →  ", p.Doses.OrderBy(d => d.DoseNumber).Select(d =>
            d.MinIntervalValue is int mv && d.OffsetValue == 0 && d.DoseNumber > 1
                ? $"{d.Label} (+{DateMath.Describe(mv, d.MinIntervalUnit!.Value)})"
                : $"{d.Label} ({(p.Anchor == ScheduleAnchor.DateOfBirth ? (d.OffsetValue == 0 ? "birth" : DateMath.Describe(d.OffsetValue, d.OffsetUnit)) : (d.OffsetUnit == IntervalUnit.Days ? "day " + d.OffsetValue : DateMath.Describe(d.OffsetValue, d.OffsetUnit)))})"));
        Reference = p.Reference;
        CatchUp = p.CatchUpRule;
        Warnings = p.Warnings;
    }
    public string Name { get; }
    public string Group { get; }
    public string Approved { get; }
    public string Doses { get; }
    public string? Reference { get; }
    public string? CatchUp { get; }
    public string? Warnings { get; }
}

public sealed class AppointmentsViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private AppointmentFilter _filter;
    private AppointmentRow? _selected;
    private string _search = "";

    public AppointmentsViewModel(ShellViewModel shell, AppointmentFilter filter)
    {
        _shell = shell;
        _filter = filter;
        _initial = filter;
        CompleteCommand = new RelayCommand(Complete, () => Selected is not null && Session.Can(Module.Vaccination));
        RescheduleCommand = new RelayCommand(Reschedule, () => Selected is not null && Session.Can(Module.Appointments));
        NotArrivedCommand = new RelayCommand(NotArrived, () => Selected is not null && Session.Can(Module.Appointments));
        OpenCommand = new RelayCommand(() => { if (Selected is not null) _shell.Navigate(new PatientProfileViewModel(_shell, Selected.PatientId)); }, () => Selected is not null);
        PrintCommand = new RelayCommand(() => Printer.Show(Pdf.AppointmentList(SettingsService.Load(), FilterTitle, Rows), "Appointments", null, FilterTitle));
        ExcelCommand = new RelayCommand(Excel);
        FilterCommand = new RelayCommand(p => { if (p is string s && Enum.TryParse<AppointmentFilter>(s, out var f)) Filter = f; });
        SearchCommand = new RelayCommand(Refresh);
        Refresh();
    }

    private readonly AppointmentFilter _initial;
    public override string Title => _initial == AppointmentFilter.Overdue ? "Missed / Overdue Doses" : "Upcoming Vaccinations & Reminders";
    public AppointmentFilter Filter { get => _filter; set { Set(ref _filter, value); Raise(nameof(FilterTitle)); Refresh(); } }
    public string FilterTitle => Filter switch
    {
        AppointmentFilter.Today => "Today's Due Doses",
        AppointmentFilter.Tomorrow => "Tomorrow's Due Doses",
        AppointmentFilter.Next7Days => "Due in the Next 7 Days",
        AppointmentFilter.Next30Days => "Due in the Next 30 Days",
        AppointmentFilter.Overdue => "Overdue Doses",
        _ => "All Upcoming Doses"
    };
    public string Search { get => _search; set => Set(ref _search, value); }
    public ObservableCollection<AppointmentRow> Rows { get; } = new();
    public AppointmentRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Count => $"{Rows.Count} dose(s)";
    public string SmsNote => "SMS / WhatsApp reminders are not connected. Use the mobile numbers in this list (print or Excel) to call or message patients.";

    public RelayCommand CompleteCommand { get; }
    public RelayCommand RescheduleCommand { get; }
    public RelayCommand NotArrivedCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand PrintCommand { get; }
    public RelayCommand ExcelCommand { get; }
    public RelayCommand FilterCommand { get; }
    public RelayCommand SearchCommand { get; }

    public override void Refresh()
    {
        Rows.Clear();
        foreach (var r in ScheduleService.Appointments(Filter, Search)) Rows.Add(r);
        Raise(nameof(Count));
    }

    private void Complete()
    {
        if (Selected is null) return;
        _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsForAppointment(Selected.AppointmentId)));
    }

    private void Reschedule()
    {
        if (Selected is null) return;
        var d = Dialogs.AskDate("Reschedule appointment", $"New date for {Selected.PatientName} – {Selected.Vaccine} {Selected.DoseLabel}:",
            Selected.DueDate < Fmt.Today ? Fmt.Today : Selected.DueDate);
        if (d is null) return;
        var reason = Dialogs.AskText("Reason", "Reason for rescheduling (optional):") ?? "";
        ScheduleService.Reschedule(Selected.AppointmentId, d.Value, reason);
        Refresh();
    }

    private void NotArrived()
    {
        if (Selected is null) return;
        ScheduleService.MarkNotArrived(Selected.AppointmentId);
        Refresh();
    }

    private void Excel()
    {
        var path = Ui.SaveExcelDialog(FilterTitle);
        if (path is null) return;
        Reporting.Excel.Export<AppointmentRow>(path, FilterTitle, Rows,
            ("Due date", r => r.DueText), ("Patient ID", r => r.PatientNo), ("Patient", r => r.PatientName), ("Mobile", r => r.Mobile),
            ("Vaccine", r => r.Vaccine), ("Dose", r => r.DoseLabel), ("Previous dose", r => r.PreviousText), ("Status", r => r.Status),
            ("Days overdue", r => r.DaysOverdue), ("Note", r => r.Note));
        Ui.Info("Saved: " + path);
    }
}

public sealed class TodayViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private AppointmentRow? _selected;

    public TodayViewModel(ShellViewModel shell)
    {
        _shell = shell;
        GiveCommand = new RelayCommand(() =>
        {
            if (Selected is not null) _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsForAppointment(Selected.AppointmentId)));
        }, () => Selected is not null && Session.Can(Module.Vaccination));
        OpenCommand = new RelayCommand(() => { if (Selected is not null) _shell.Navigate(new PatientProfileViewModel(_shell, Selected.PatientId)); }, () => Selected is not null);
        PrintCommand = new RelayCommand(() =>
        {
            var r = ReportService.Period(Fmt.Today, Fmt.Today, "Daily Vaccination Report");
            Printer.Show(Pdf.PeriodReport(SettingsService.Load(), r), "Daily-report", null, "Daily Vaccination Report");
        });
        Refresh();
    }

    public override string Title => "Today's Vaccinations";
    public ObservableCollection<AppointmentRow> Due { get; } = new();
    public ObservableCollection<VaccinationRow> Given { get; } = new();
    public AppointmentRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public string DueTitle => $"Due today ({Due.Count})";
    public string GivenTitle => $"Given today ({Given.Count})";
    public RelayCommand GiveCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand PrintCommand { get; }

    public override void Refresh()
    {
        Due.Clear();
        foreach (var a in ScheduleService.Appointments(AppointmentFilter.Today)) Due.Add(a);
        Given.Clear();
        foreach (var g in VaccinationService.History(Fmt.Today, Fmt.Today)) Given.Add(g);
        Raise(nameof(DueTitle));
        Raise(nameof(GivenTitle));
    }
}

public sealed class HistoryViewModel : PageViewModel
{
    private VaccineOption? _vaccine;
    public HistoryViewModel(ShellViewModel shell)
    {
        Vaccines = new List<VaccineOption> { new(0, "All vaccines") };
        Vaccines.AddRange(VaccinationService.Vaccines(true));
        _vaccine = Vaccines[0];
        SearchCommand = new RelayCommand(Refresh);
        ExcelCommand = new RelayCommand(Excel);
        PrintCommand = new RelayCommand(() =>
        {
            var r = ReportService.Period(Ui.ToDateOnly(From) ?? Fmt.Today, Ui.ToDateOnly(To) ?? Fmt.Today, "Vaccination History");
            Printer.Show(Pdf.PeriodReport(SettingsService.Load(), r), "History", null, "Vaccination History");
        });
        Refresh();
    }

    public override string Title => "Vaccination History";
    public DateTime? From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime? To { get; set; } = DateTime.Today;
    public string Search { get; set; } = "";
    public List<VaccineOption> Vaccines { get; }
    public VaccineOption? Vaccine { get => _vaccine; set => Set(ref _vaccine, value); }
    public ObservableCollection<VaccinationRow> Rows { get; } = new();
    public string Count => $"{Rows.Count} record(s)";
    public RelayCommand SearchCommand { get; }
    public RelayCommand ExcelCommand { get; }
    public RelayCommand PrintCommand { get; }

    public override void Refresh()
    {
        Rows.Clear();
        foreach (var r in VaccinationService.History(Ui.ToDateOnly(From), Ui.ToDateOnly(To), Search, Vaccine is { Id: > 0 } v ? v.Id : null)) Rows.Add(r);
        Raise(nameof(Count));
    }

    private void Excel()
    {
        var path = Ui.SaveExcelDialog("Vaccination-History");
        if (path is null) return;
        Reporting.Excel.Export<VaccinationRow>(path, "Vaccination History", Rows,
            ("Record", r => r.RecordNo), ("Date", r => r.DateText), ("Time", r => r.TimeText), ("Patient ID", r => r.PatientNo), ("Patient", r => r.PatientName),
            ("Vaccine", r => r.Vaccine), ("Dose", r => r.Dose), ("Volume", r => r.Volume), ("Route", r => r.Route), ("Site", r => r.Site),
            ("Batch", r => r.Batch), ("Administered by", r => r.AdministeredBy), ("Old card", r => r.Historical ? "Yes" : ""), ("Remarks", r => r.Remarks));
        Ui.Info("Saved: " + path);
    }
}

public sealed class DoctorsViewModel : PageViewModel
{
    private DoctorRow? _selected;
    public DoctorsViewModel(ShellViewModel shell)
    {
        NewCommand = new RelayCommand(() => { Selected = null; EditName = EditSpecialization = EditHospital = EditContact = ""; RaiseAll(); });
        SaveCommand = new RelayCommand(Save, () => Session.Can(Module.Doctors));
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null && Session.Can(Module.Settings));
        SearchCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Title => "Doctor Directory";
    public string Search { get; set; } = "";
    public ObservableCollection<DoctorRow> Rows { get; } = new();
    public DoctorRow? Selected
    {
        get => _selected;
        set
        {
            Set(ref _selected, value);
            if (value is null) return;
            EditName = value.Name; EditSpecialization = value.Specialization ?? ""; EditHospital = value.Hospital ?? ""; EditContact = value.ContactNumber ?? "";
            Raise(nameof(EditName)); Raise(nameof(EditSpecialization)); Raise(nameof(EditHospital)); Raise(nameof(EditContact));
        }
    }
    public string EditName { get; set; } = "";
    public string EditSpecialization { get; set; } = "";
    public string EditHospital { get; set; } = "";
    public string EditContact { get; set; } = "";
    public bool CanDelete => Session.Can(Module.Settings);
    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SearchCommand { get; }

    public override void Refresh()
    {
        Rows.Clear();
        foreach (var d in DoctorService.List(Search)) Rows.Add(d);
    }

    private void Save()
    {
        DoctorService.Save(Selected?.Id, EditName, EditSpecialization, EditHospital, EditContact);
        Refresh();
        Ui.Info("Doctor saved.");
    }

    private void Delete()
    {
        if (Selected is null) return;
        if (!Ui.Confirm($"Delete {Selected.Name}? Patients keep the doctor's name in their record.")) return;
        DoctorService.Delete(Selected.Id);
        Selected = null;
        Refresh();
    }
}

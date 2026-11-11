using System.Collections.ObjectModel;
using System.Windows;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Reporting;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

public static class Lists
{
    public static Array Genders => Enum.GetValues(typeof(Gender));
    public static Array Categories => Enum.GetValues(typeof(PatientCategory));
    public static Array Routes => Enum.GetValues(typeof(AdministrationRoute));
    public static Array Animals => Enum.GetValues(typeof(AnimalType));
    public static Array BiteCategories => Enum.GetValues(typeof(BiteCategory));
    public static Array RigTypes => Enum.GetValues(typeof(RigType));
    public static Array VvmStates => Enum.GetValues(typeof(VvmStatus));
    public static Array Roles => Enum.GetValues(typeof(UserRole));
    public static Array Units => Enum.GetValues(typeof(IntervalUnit));
    public static Array Anchors => Enum.GetValues(typeof(ScheduleAnchor));
    public static Array Kinds => Enum.GetValues(typeof(ProtocolKind));
}

public sealed class PatientEntryViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly int? _id;
    private DateTime? _dob;
    private string _age = "";

    public PatientEntryViewModel(ShellViewModel shell, int? id)
    {
        _shell = shell;
        _id = id;
        DoctorNames = DoctorService.Names();
        if (id is int pid)
        {
            var i = PatientService.LoadInput(pid);
            PatientNo = $"BNV-{pid:D6}";
            Name = i.Name; Guardian = i.GuardianName ?? ""; _dob = Ui.ToDateTime(i.DateOfBirth); Age = i.AgeYears?.ToString() ?? "";
            Gender = i.Gender; Address = i.Address ?? ""; Cnic = i.Cnic ?? ""; Mobile = i.Mobile ?? ""; AltMobile = i.AlternativeMobile ?? "";
            AdvisedBy = i.AdvisedBy ?? ""; RegistrationDate = Ui.ToDateTime(i.RegistrationDate); Category = i.Category; Remarks = i.Remarks ?? "";
            Immunocompromised = i.IsImmunocompromised;
        }
        UpdateAgeText();
        SaveCommand = new RelayCommand(() => Save(false));
        ClearCommand = new RelayCommand(() => _shell.Replace(new PatientEntryViewModel(_shell, _id)));
    }

    public override string Title => _id is null ? "New Patient Entry" : "Edit Patient";
    public string PatientNo { get; } = "Generated automatically";
    public List<string> DoctorNames { get; }
    public string Name { get; set; } = "";
    public string Guardian { get; set; } = "";
    public DateTime? DateOfBirth { get => _dob; set { Set(ref _dob, value); UpdateAgeText(); } }
    public string Age { get; set; } = "";
    public string AgeText { get => _age; private set => Set(ref _age, value); }
    public Gender Gender { get; set; } = Gender.Male;
    public string Address { get; set; } = "";
    public string Cnic { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string AltMobile { get; set; } = "";
    public string AdvisedBy { get; set; } = "";
    public DateTime? RegistrationDate { get; set; } = DateTime.Today;
    public PatientCategory Category { get; set; } = PatientCategory.General;
    public string Remarks { get; set; } = "";
    public bool Immunocompromised { get; set; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ClearCommand { get; }

    private void UpdateAgeText() => AgeText = _dob is DateTime d ? "Age: " + DateMath.Age(DateOnly.FromDateTime(d), Fmt.Today).Text : "";

    private void Save(bool allowSimilar)
    {
        var input = new PatientInput
        {
            Id = _id, Name = Name, GuardianName = Guardian, DateOfBirth = Ui.ToDateOnly(DateOfBirth), Gender = Gender, Address = Address, Cnic = Cnic,
            Mobile = Mobile, AlternativeMobile = AltMobile, AdvisedBy = AdvisedBy, RegistrationDate = Ui.ToDateOnly(RegistrationDate) ?? Fmt.Today,
            Category = Category, Remarks = Remarks, IsImmunocompromised = Immunocompromised
        };
        if (!string.IsNullOrWhiteSpace(Age))
        {
            input.AgeYears = Ui.ParseInt(Age) ?? throw new UserException("Age must be a number of years.");
        }
        else if (input.DateOfBirth is null) throw new UserException("Enter the age or the date of birth.");

        try
        {
            var id = PatientService.Register(input, allowSimilar);
            Ui.Info(_id is null ? "Patient registered successfully." : "Patient updated.");
            _shell.Replace(new PatientProfileViewModel(_shell, id));
        }
        catch (PossibleDuplicateException ex)
        {
            var list = string.Join("\n", ex.Matches.Select(m => $"  {m.PatientNo} – {m.Name} ({m.GuardianName}) {m.Mobile}"));
            var answer = MessageBox.Show(ex.Message + "\n\n" + list + "\n\nYES = it is a different person, save as NEW patient\nNO = open the existing patient\nCANCEL = go back",
                Ui.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) Save(true);
            else if (answer == MessageBoxResult.No) _shell.Replace(new PatientProfileViewModel(_shell, ex.Matches[0].Id));
        }
    }
}

public sealed class PatientSearchViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private string _text = "";
    private PatientService.SearchField _field = PatientService.SearchField.Any;
    private PatientRow? _selected;

    public PatientSearchViewModel(ShellViewModel shell, Func<PatientRow, PageViewModel>? onOpen = null, string? title = null)
    {
        _shell = shell;
        _onOpen = onOpen;
        _title = title;
        SearchCommand = new RelayCommand(Search);
        OpenCommand = new RelayCommand(Open, () => Selected is not null);
        Search();
    }

    private readonly Func<PatientRow, PageViewModel>? _onOpen;
    private readonly string? _title;
    public override string Title => _title ?? "Old Patient Search";
    public Array Fields => Enum.GetValues(typeof(PatientService.SearchField));
    public string Text { get => _text; set { Set(ref _text, value); } }
    public PatientService.SearchField Field { get => _field; set => Set(ref _field, value); }
    public ObservableCollection<PatientRow> Results { get; } = new();
    public PatientRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Count => $"{Results.Count} patient(s)";
    public RelayCommand SearchCommand { get; }
    public RelayCommand OpenCommand { get; }

    public override void Refresh() => Search();

    private void Search()
    {
        Results.Clear();
        foreach (var r in PatientService.Search(Text, Field)) Results.Add(r);
        Raise(nameof(Count));
    }

    public void Open()
    {
        if (Selected is null) return;
        _shell.Navigate(_onOpen is null ? new PatientProfileViewModel(_shell, Selected.Id) : _onOpen(Selected));
    }
}

public sealed class PatientProfileViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly int _id;
    private PatientProfile _p = null!;
    private DoseRow? _selectedDose;
    private VaccineOption? _vaccine;
    private ProtocolItem? _protocol;

    public PatientProfileViewModel(ShellViewModel shell, int id)
    {
        _shell = shell;
        _id = id;
        Vaccines = VaccinationService.Vaccines();
        EditCommand = new RelayCommand(() => _shell.Navigate(new PatientEntryViewModel(_shell, _id)), () => Session.Can(Module.Patients));
        GiveDoseCommand = new RelayCommand(GiveSelected, () => SelectedDose is not null && SelectedDose.Status != "Completed" && Session.Can(Module.Vaccination));
        NewVaccinationCommand = new RelayCommand(NewVaccination, () => Session.Can(Module.Vaccination));
        StartCourseCommand = new RelayCommand(StartCourse, () => Session.Can(Module.Vaccination));
        RescheduleCommand = new RelayCommand(Reschedule, () => SelectedDose is not null && SelectedDose.Status != "Completed");
        NotArrivedCommand = new RelayCommand(NotArrived, () => SelectedDose is not null && SelectedDose.Status != "Completed");
        StopCourseCommand = new RelayCommand(StopCourse, () => SelectedDose is not null && Session.Can(Module.Vaccination));
        InsulinCommand = new RelayCommand(() => _shell.Navigate(new InsulinViewModel(_shell, _id)), () => Session.Can(Module.Insulin));
        RabiesCommand = new RelayCommand(() => _shell.Navigate(new RabiesViewModel(_shell, _id)), () => Session.Can(Module.AntiRabies));
        PrintCardCommand = new RelayCommand(() => Print("card"));
        PrintHistoryCommand = new RelayCommand(() => Print("history"));
        PrintScheduleCommand = new RelayCommand(() => Print("schedule"));
        PrintFormCommand = new RelayCommand(() => Print("form"));
        Refresh();
    }

    public override string Title => "Patient Profile";
    public PatientProfile P { get => _p; private set => Set(ref _p, value); }
    public string Header => $"{P.Patient.PatientNo}  ·  {P.Patient.Name}";
    public string SubHeader => string.Join("   ·   ", new[] { P.Patient.GuardianName is null ? null : "S/O, D/O, W/O " + P.Patient.GuardianName, P.AgeText,
        P.Patient.Gender.ToString(), P.Patient.Mobile, P.Patient.Cnic, Fmt.Category(P.Patient.Category) }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string NextText => P.NextAppointment is { } n ? $"{n.DueText} – {n.Vaccine} {n.DoseLabel} ({n.Status})" : "No pending doses";
    public string Visits => string.Join(",  ", P.VisitDates.Take(12).Select(d => Fmt.D(d)));
    public bool HasReview => P.Doses.Any(d => d.NeedsReview && d.Status != "Completed") || P.Patient.IsImmunocompromised;
    public string ReviewText => P.Patient.IsImmunocompromised ? "Immunocompromised patient – vaccination decisions need clinical review." : "One or more courses are flagged for clinical review (late doses or special category).";
    public bool IsNewborn => P.Patient.Newborn is not null;
    public string DobText => Fmt.D(P.Patient.DateOfBirth);
    public string RegisteredText => Fmt.D(P.Patient.RegistrationDate);
    public string CategoryText => Fmt.Category(P.Patient.Category);

    public List<VaccineOption> Vaccines { get; }
    public VaccineOption? Vaccine
    {
        get => _vaccine;
        set { Set(ref _vaccine, value); Protocols = value is null ? new List<ProtocolItem>() : ScheduleService.Protocols(value.Id); Raise(nameof(Protocols)); Protocol = Protocols.FirstOrDefault(); }
    }
    public List<ProtocolItem> Protocols { get; private set; } = new();
    public ProtocolItem? Protocol { get => _protocol; set => Set(ref _protocol, value); }
    public DateTime? FirstDoseDate { get; set; } = DateTime.Today;

    public DoseRow? SelectedDose { get => _selectedDose; set => Set(ref _selectedDose, value); }

    public RelayCommand EditCommand { get; }
    public RelayCommand GiveDoseCommand { get; }
    public RelayCommand NewVaccinationCommand { get; }
    public RelayCommand StartCourseCommand { get; }
    public RelayCommand RescheduleCommand { get; }
    public RelayCommand NotArrivedCommand { get; }
    public RelayCommand StopCourseCommand { get; }
    public RelayCommand InsulinCommand { get; }
    public RelayCommand RabiesCommand { get; }
    public RelayCommand PrintCardCommand { get; }
    public RelayCommand PrintHistoryCommand { get; }
    public RelayCommand PrintScheduleCommand { get; }
    public RelayCommand PrintFormCommand { get; }

    public override void Refresh()
    {
        P = PatientService.Profile(_id);
        RaiseAll();
    }

    private void GiveSelected()
    {
        if (SelectedDose is null) return;
        _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsForAppointment(SelectedDose.AppointmentId)));
    }

    private void NewVaccination()
    {
        if (Vaccine is null) throw new UserException("Select the vaccine.");
        _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsFor(_id, Vaccine.Id, Protocol?.Id)));
    }

    /// <summary>Plans a course without giving a dose now (for example when the first dose was given elsewhere and the date is known).</summary>
    private void StartCourse()
    {
        if (Protocol is null) throw new UserException("Select the vaccine and the protocol.");
        if (!Protocol.Approved && !Ui.Confirm("This protocol has not yet been approved by your center's medical officer. Continue?")) return;
        var anchor = Ui.ToDateOnly(FirstDoseDate) ?? Fmt.Today;
        ScheduleService.StartCourse(_id, Protocol.Id, anchor);
        Ui.Info("The schedule was created. Each dose now appears in Upcoming Vaccinations. Record dose 1 from the schedule list when it is given.");
        Refresh();
    }

    private void Reschedule()
    {
        if (SelectedDose is null) return;
        var d = Dialogs.AskDate("Reschedule", $"New date for {SelectedDose.Vaccine} {SelectedDose.DoseLabel} (earliest {Fmt.D(SelectedDose.CalculatedDate)}):",
            SelectedDose.DueDate < Fmt.Today ? Fmt.Today : SelectedDose.DueDate);
        if (d is null) return;
        ScheduleService.Reschedule(SelectedDose.AppointmentId, d.Value, "Rescheduled from patient profile");
        Refresh();
    }

    private void NotArrived()
    {
        if (SelectedDose is null) return;
        ScheduleService.MarkNotArrived(SelectedDose.AppointmentId);
        Refresh();
    }

    private void StopCourse()
    {
        if (SelectedDose is null) return;
        var reason = Dialogs.AskText("Stop course", $"Stop the whole course '{SelectedDose.Protocol}'? Pending doses will be cancelled. Write the reason:");
        if (reason is null) return;
        ScheduleService.StopCourse(SelectedDose.CourseId, reason);
        Refresh();
    }

    private void Print(string kind)
    {
        var c = SettingsService.Load();
        switch (kind)
        {
            case "card" when IsNewborn: Printer.Show(Pdf.NewbornCard(c, P), $"Newborn-card-{P.Patient.PatientNo}", _id, "Newborn Immunization Card"); break;
            case "card": Printer.Show(Pdf.VaccinationCard(c, P), $"Vaccination-card-{P.Patient.PatientNo}", _id, "Vaccination Card"); break;
            case "history": Printer.Show(Pdf.PatientHistory(c, P), $"History-{P.Patient.PatientNo}", _id, "Patient Vaccination History"); break;
            case "schedule": Printer.Show(Pdf.UpcomingSchedule(c, P), $"Schedule-{P.Patient.PatientNo}", _id, "Upcoming Dose Schedule"); break;
            default: Printer.Show(Pdf.RegistrationForm(c, P), $"Registration-{P.Patient.PatientNo}", _id, "Patient Registration Form"); break;
        }
        Refresh();
    }
}

public sealed class NewbornViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private DateTime? _dob = DateTime.Today;
    private string _age = "";

    public NewbornViewModel(ShellViewModel shell)
    {
        _shell = shell;
        DoctorNames = DoctorService.Names();
        SaveCommand = new RelayCommand(Save);
        UpdateAge();
    }

    public override string Title => "Newborn Baby Registration";
    public List<string> DoctorNames { get; }
    public string BabyName { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Male;
    public DateTime? DateOfBirth { get => _dob; set { Set(ref _dob, value); UpdateAge(); } }
    public string TimeOfBirth { get; set; } = "";
    public string FatherName { get; set; } = "";
    public string MotherName { get; set; } = "";
    public string MotherMobile { get; set; } = "";
    public string Address { get; set; } = "";
    public string BirthPlace { get; set; } = "";
    public string BirthWeight { get; set; } = "";
    public DateTime? RegistrationDate { get; set; } = DateTime.Today;
    public string AdvisedBy { get; set; } = "";
    public string AgeText { get => _age; private set => Set(ref _age, value); }
    public ObservableCollection<PreviewDose> Timeline { get; } = new();
    public RelayCommand SaveCommand { get; }

    private void UpdateAge()
    {
        Timeline.Clear();
        if (_dob is not DateTime d) { AgeText = ""; return; }
        var dob = DateOnly.FromDateTime(d);
        var a = DateMath.Age(dob, Fmt.Today);
        AgeText = $"Age: {a.Days} day(s)  ·  {a.Weeks} week(s)  ·  {a.Months} month(s)";
        try
        {
            foreach (var p in ScheduleService.Protocols().Where(p => p.Kind == ProtocolKind.ChildhoodEpi))
                foreach (var dose in ScheduleService.Preview(p.Id, dob))
                    Timeline.Add(dose with { Label = $"{p.VaccineName}: {dose.Label}" });
            var sorted = Timeline.OrderBy(t => t.Date).ThenBy(t => t.Label).ToList();
            Timeline.Clear();
            foreach (var t in sorted) Timeline.Add(t);
        }
        catch { }
    }

    private void Save()
    {
        if (DateOfBirth is null) throw new UserException("Enter the date of birth.");
        TimeOnly? tob = null;
        if (!string.IsNullOrWhiteSpace(TimeOfBirth))
        {
            if (!TimeOnly.TryParse(TimeOfBirth, System.Globalization.CultureInfo.InvariantCulture, out var t)) throw new UserException("Time of birth must be like 08:30 or 8:30 PM.");
            tob = t;
        }
        decimal? weight = null;
        if (!string.IsNullOrWhiteSpace(BirthWeight)) weight = Ui.ParseDecimal(BirthWeight) ?? throw new UserException("Birth weight must be a number in kg, e.g. 3.1");

        var id = PatientService.RegisterNewborn(new NewbornInput
        {
            BabyName = BabyName, Gender = Gender, DateOfBirth = DateOnly.FromDateTime(DateOfBirth.Value), TimeOfBirth = tob, FatherName = FatherName,
            MotherName = MotherName, MotherMobile = MotherMobile, Address = Address, BirthPlace = BirthPlace, BirthWeightKg = weight,
            RegistrationDate = Ui.ToDateOnly(RegistrationDate) ?? Fmt.Today, AdvisedBy = AdvisedBy
        });
        Ui.Info("Newborn registered. The complete EPI vaccination schedule has been created from the date of birth.");
        _shell.Replace(new PatientProfileViewModel(_shell, id));
    }
}

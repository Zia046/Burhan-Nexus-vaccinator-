using System.Collections.ObjectModel;
using System.Globalization;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Reporting;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

/// <summary>Vaccine administration entry. Saving updates history, stock, dose status, next dates and the dashboard in one transaction.</summary>
public sealed class AdministerViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private readonly AdministrationDefaults _d;
    private DateTime? _date = DateTime.Today;
    private bool _historical;
    private BatchOption? _batch;

    public AdministerViewModel(ShellViewModel shell, AdministrationDefaults defaults, int? rabiesCaseId = null)
    {
        _shell = shell;
        _d = defaults;
        _rabiesCaseId = rabiesCaseId;
        Volume = defaults.DoseVolume ?? "";
        Route = defaults.Route;
        Site = defaults.InjectionSite ?? "";
        AdministeredBy = Session.DisplayName;
        Time = Fmt.T(TimeOnly.FromDateTime(DateTime.Now));
        SaveCommand = new RelayCommand(() => Save(false));
        CancelCommand = new RelayCommand(_shell.GoBack);
        LoadBatches();
    }

    private readonly int? _rabiesCaseId;
    public override string Title => "Vaccine Administration Entry";
    public string Patient => $"{_d.PatientNo} – {_d.PatientName}";
    public string Vaccine => _d.VaccineName;
    public string Protocol => _d.ProtocolName ?? "(select the protocol on the patient profile)";
    public string Dose => _d.DoseLabel == $"Dose {_d.DoseNumber}" ? _d.DoseLabel : $"{_d.DoseLabel} (dose {_d.DoseNumber})";
    public string Due => _d.DueDate is DateOnly d ? Fmt.D(d) : "First dose – today starts the schedule";
    public string? Warnings => _d.Warnings;
    public string? Review => _d.ReviewReason;
    public bool NotApproved => _d.ProtocolId is not null && !_d.ProtocolApproved;

    public DateTime? Date { get => _date; set { if (Set(ref _date, value)) LoadBatches(); } }
    public string Time { get; set; }
    public string Volume { get; set; }
    public AdministrationRoute Route { get; set; }
    public string Site { get; set; }
    public ObservableCollection<BatchOption> Batches { get; } = new();
    public BatchOption? Batch { get => _batch; set { Set(ref _batch, value); Raise(nameof(BatchExpiry)); } }
    public string BatchExpiry => Batch is null ? "" : "Expiry: " + Fmt.D(Batch.Expiry);
    public bool NoBatches => Batches.Count == 0 && !Historical;
    public bool Historical
    {
        get => _historical;
        set { Set(ref _historical, value); Raise(nameof(NotHistorical)); Raise(nameof(NoBatches)); }
    }
    public bool NotHistorical => !Historical;
    public string ManualBatch { get; set; } = "";
    public DateTime? ManualExpiry { get; set; }
    public string AdministeredBy { get; set; }
    public string Remarks { get; set; } = "";
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void LoadBatches()
    {
        Batches.Clear();
        var on = Ui.ToDateOnly(Date) ?? Fmt.Today;
        foreach (var b in InventoryService.UsableBatches(_d.VaccineId, on)) Batches.Add(b);
        Batch = Batches.FirstOrDefault();
        Raise(nameof(NoBatches));
    }

    private void Save(bool confirmed)
    {
        if (NotApproved && !confirmed && !Ui.Confirm("This protocol has not yet been approved by your center's medical officer (Settings → Vaccine Protocols). Continue?"))
            return;
        if (!TimeOnly.TryParse(Time, CultureInfo.InvariantCulture, out var time)) throw new UserException("Enter the time like 10:30 or 10:30 AM.");
        var input = new AdministrationInput
        {
            PatientId = _d.PatientId, AppointmentId = _d.AppointmentId, VaccineId = _d.VaccineId, ProtocolId = _d.ProtocolId, DoseNumber = _d.DoseNumber,
            Date = Ui.ToDateOnly(Date) ?? Fmt.Today, Time = time, DoseVolume = Volume, Route = Route, InjectionSite = Site,
            BatchId = Historical ? null : Batch?.Id, BatchNumber = Historical ? ManualBatch : null, ExpiryDate = Historical ? Ui.ToDateOnly(ManualExpiry) : null,
            AdministeredBy = AdministeredBy, Remarks = Remarks, Historical = Historical, AntiRabiesCaseId = _rabiesCaseId
        };
        AdministrationResult result;
        try
        {
            result = VaccinationService.Administer(input, confirmed);
        }
        catch (ConfirmationNeededException ex)
        {
            if (Ui.Confirm(ex.Message + "\n\nSave anyway?")) Save(true);
            return;
        }

        if (Ui.Confirm(result.Message + "\n\nPrint the vaccination record / card now?"))
        {
            var c = SettingsService.Load();
            var p = PatientService.Profile(result.PatientId);
            var caseId = _rabiesCaseId ?? (_d.VaccineName == "Rabies" ? p.RabiesCases.FirstOrDefault()?.Id : null);
            if (caseId is int id)
                Printer.Show(Pdf.AntiRabiesCard(c, p, RabiesService.Load(id)), $"AntiRabies-{p.Patient.PatientNo}", p.Patient.Id, "Anti-Rabies Vaccination Card");
            else if (p.Patient.Newborn is not null)
                Printer.Show(Pdf.NewbornCard(c, p), $"Newborn-card-{p.Patient.PatientNo}", p.Patient.Id, "Newborn Immunization Card");
            else
                Printer.Show(Pdf.VaccinationCard(c, p), $"Vaccination-card-{p.Patient.PatientNo}", p.Patient.Id, "Vaccination Card");
        }
        _shell.GoBack();
    }
}

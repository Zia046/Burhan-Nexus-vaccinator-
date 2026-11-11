using System.Collections.ObjectModel;
using System.Windows;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Reporting;

namespace BurhanNexus.Vaccinator.Desktop.ViewModels;

// ============================================================================ Anti-Rabies

public sealed class RabiesViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private PatientRow? _existing;
    private RabiesList _list = RabiesList.All;
    private RabiesCaseRow? _selected;
    private bool _previouslyVaccinated;
    private BiteCategory _category = BiteCategory.II;
    private ProtocolItem? _protocol;

    public RabiesViewModel(ShellViewModel shell, int? patientId = null)
    {
        _shell = shell;
        DoctorNames = DoctorService.Names();
        var rabies = VaccinationService.Vaccines().FirstOrDefault(v => v.Name == "Rabies");
        Protocols = rabies is null ? new List<ProtocolItem>() : ScheduleService.Protocols(rabies.Id);
        Protocol = Protocols.FirstOrDefault();
        if (patientId is int pid)
        {
            Existing = PatientService.Row(pid);
            TabIndex = 0;
        }
        FindCommand = new RelayCommand(Find);
        ClearPatientCommand = new RelayCommand(() => Existing = null);
        SaveCommand = new RelayCommand(() => Save(false));
        GiveCommand = new RelayCommand(Give, () => Selected is not null && Session.Can(Module.Vaccination));
        OpenCommand = new RelayCommand(() => { if (Selected is not null) _shell.Navigate(new PatientProfileViewModel(_shell, Selected.PatientId)); }, () => Selected is not null);
        PrintCommand = new RelayCommand(PrintCard, () => Selected is not null);
        ListCommand = new RelayCommand(p => { if (p is string s && Enum.TryParse<RabiesList>(s, out var l)) CurrentList = l; });
        SearchCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Title => "Anti-Rabies Management";
    public int TabIndex { get; set; }
    public List<string> DoctorNames { get; }
    public List<ProtocolItem> Protocols { get; }

    // ---- registration
    public string FindText { get; set; } = "";
    public PatientRow? Existing { get => _existing; set { Set(ref _existing, value); Raise(nameof(IsNewPatient)); Raise(nameof(ExistingText)); } }
    public bool IsNewPatient => Existing is null;
    public string ExistingText => Existing is null ? "" : $"Existing patient: {Existing.PatientNo} – {Existing.Name} ({Existing.GuardianName}) {Existing.Mobile}";
    public string Name { get; set; } = "";
    public string Guardian { get; set; } = "";
    public string Age { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Male;
    public string Address { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Cnic { get; set; } = "";
    public DateTime? BiteDate { get; set; } = DateTime.Today;
    public DateTime? FirstVaccineDate { get; set; } = DateTime.Today;
    public AnimalType Animal { get; set; } = AnimalType.Dog;
    public string AnimalOther { get; set; } = "";
    public BiteCategory Category { get => _category; set { Set(ref _category, value); Raise(nameof(IsCategory3)); } }
    public bool IsCategory3 => Category == BiteCategory.III;
    public string BiteLocation { get; set; } = "";
    public bool PreviouslyVaccinated { get => _previouslyVaccinated; set { Set(ref _previouslyVaccinated, value); } }
    public string PreviousDetails { get; set; } = "";
    public string VaccineBrand { get; set; } = "";
    public ProtocolItem? Protocol { get => _protocol; set { Set(ref _protocol, value); Raise(nameof(ProtocolInfo)); } }
    public string ProtocolInfo => Protocol is null ? "" : $"{Protocol.TargetGroup}. {Protocol.ApprovalText}. Ref: {Protocol.Reference}";
    public bool WoundWashed { get; set; }
    public RigType Rig { get; set; } = RigType.None;
    public string RigDose { get; set; } = "";
    public string RigBatch { get; set; } = "";
    public DateTime? RigDate { get; set; } = DateTime.Today;
    public bool Immunocompromised { get; set; }
    public string AdvisedBy { get; set; } = "";
    public string Remarks { get; set; } = "";
    public bool GiveFirstDoseNow { get; set; } = true;

    // ---- lists
    public RabiesList CurrentList { get => _list; set { Set(ref _list, value); Refresh(); } }
    public string Search { get; set; } = "";
    public ObservableCollection<RabiesCaseRow> Cases { get; } = new();
    public RabiesCaseRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Count => $"{Cases.Count} case(s) – {CurrentList}";

    public RelayCommand FindCommand { get; }
    public RelayCommand ClearPatientCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand GiveCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand PrintCommand { get; }
    public RelayCommand ListCommand { get; }
    public RelayCommand SearchCommand { get; }

    public override void Refresh()
    {
        Cases.Clear();
        foreach (var c in RabiesService.Cases(CurrentList, Search)) Cases.Add(c);
        Raise(nameof(Count));
    }

    private void Find()
    {
        var found = PatientService.Search(FindText);
        if (found.Count == 0) { Ui.Info("No patient found. Fill in the new patient details below."); return; }
        if (found.Count == 1 || Ui.Confirm($"{found.Count} patients found. Use the first one?\n\n{found[0].Display}")) Existing = found[0];
    }

    private void Save(bool allowSimilar)
    {
        if (Protocol is null) throw new UserException("Select the protocol.");
        var input = new RabiesInput
        {
            ExistingPatientId = Existing?.Id,
            NewPatient = new PatientInput
            {
                Name = Name, GuardianName = Guardian, AgeYears = Ui.ParseInt(Age), Gender = Gender, Address = Address, Mobile = Mobile, Cnic = Cnic,
                Category = PatientCategory.AntiRabies, AdvisedBy = AdvisedBy, IsImmunocompromised = Immunocompromised
            },
            BiteDate = Ui.ToDateOnly(BiteDate) ?? Fmt.Today, FirstVaccineDate = Ui.ToDateOnly(FirstVaccineDate) ?? Fmt.Today, AnimalType = Animal, AnimalOther = AnimalOther,
            Category = Category, BiteLocation = BiteLocation, PreviouslyVaccinated = PreviouslyVaccinated, PreviousVaccinationDetails = PreviousDetails,
            VaccineBrand = VaccineBrand, ProtocolId = Protocol.Id, WoundWashed = WoundWashed, Rig = Rig, RigDose = RigDose, RigBatch = RigBatch,
            RigDate = Ui.ToDateOnly(RigDate), Immunocompromised = Immunocompromised, AdvisedBy = AdvisedBy, Remarks = Remarks
        };
        if (Existing is null)
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new UserException("Search an existing patient or enter the new patient's name.");
            if (string.IsNullOrWhiteSpace(Age)) throw new UserException("Enter the patient's age.");
            if (Ui.ParseInt(Age) is null) throw new UserException("Age must be a number of years.");
        }

        if (!allowSimilar)
        {
            var warnings = RabiesService.Warnings(input);
            if (warnings.Count > 0 && MessageBox.Show(string.Join("\n\n", warnings) + "\n\nA healthcare professional must confirm the protocol. Save this case?",
                    Ui.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }

        (int CaseId, int PatientId) saved;
        try { saved = RabiesService.Register(input, allowSimilar); }
        catch (PossibleDuplicateException ex)
        {
            var answer = MessageBox.Show(ex.Message + "\n\n" + string.Join("\n", ex.Matches.Select(m => m.Display)) +
                                         "\n\nYES = different person, save as new\nNO = use the existing patient", Ui.AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) Save(true);
            else if (answer == MessageBoxResult.No) { Existing = ex.Matches[0]; Save(true); }
            return;
        }

        Refresh();
        var next = RabiesService.NextAppointment(saved.CaseId);
        if (GiveFirstDoseNow && next is int apptId && Session.Can(Module.Vaccination))
        {
            Ui.Info("Case saved and the dose schedule was created. Now record the first dose (batch, site).");
            _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsForAppointment(apptId), saved.CaseId));
        }
        else
        {
            Ui.Info("Case saved. The dose schedule was created; doses appear in the lists and reminders.");
            _shell.Replace(new RabiesViewModel(_shell) { });
        }
    }

    private void Give()
    {
        if (Selected is null) return;
        var next = RabiesService.NextAppointment(Selected.Id);
        if (next is null) { Ui.Info("This course has no pending dose."); return; }
        _shell.Navigate(new AdministerViewModel(_shell, VaccinationService.DefaultsForAppointment(next.Value), Selected.Id));
    }

    private void PrintCard()
    {
        if (Selected is null) return;
        var p = PatientService.Profile(Selected.PatientId);
        Printer.Show(Pdf.AntiRabiesCard(SettingsService.Load(), p, RabiesService.Load(Selected.Id)), $"AntiRabies-{Selected.CaseNo}", Selected.PatientId, "Anti-Rabies Vaccination Card");
    }
}

// ============================================================================ Insulin

public sealed class InsulinViewModel : PageViewModel
{
    private readonly ShellViewModel _shell;
    private PatientRow? _patient;
    private InsulinProductRow? _product;
    private InsulinBatchRow? _batch;
    private string _vials = "1";
    private string _price = "";

    public InsulinViewModel(ShellViewModel shell, int? patientId = null)
    {
        _shell = shell;
        DoctorNames = DoctorService.Names();
        if (patientId is int pid) Patient = PatientService.Row(pid);
        FindCommand = new RelayCommand(Find);
        NewPatientCommand = new RelayCommand(() => _shell.Navigate(new PatientEntryViewModel(_shell, null)));
        IssueCommand = new RelayCommand(Issue, () => Session.Can(Module.Insulin));
        SaveProductCommand = new RelayCommand(SaveProduct, () => Session.Can(Module.InsulinStock));
        NewProductCommand = new RelayCommand(() => { EditProduct = null; PBrand = PType = PStrength = PVial = PPrice = PStorage = ""; PLow = "10"; RaiseAll(); });
        StockInCommand = new RelayCommand(StockIn, () => Session.Can(Module.InsulinStock));
        WastageCommand = new RelayCommand(Wastage, () => Session.Can(Module.InsulinStock));
        ReceiptCommand = new RelayCommand(() => { if (SelectedTx is not null) PrintReceipt(SelectedTx.Id); }, () => SelectedTx is not null);
        StockReportCommand = new RelayCommand(() => Printer.Show(Pdf.InsulinStockReport(SettingsService.Load(), Batches), "Insulin-stock", null, "Insulin Stock Report"));
        DailyCommand = new RelayCommand(() => Report(Fmt.Today, Fmt.Today, "Daily Insulin Dispensing Report"));
        MonthlyCommand = new RelayCommand(() => Report(new DateOnly(Fmt.Today.Year, Fmt.Today.Month, 1), Fmt.Today, "Monthly Insulin Dispensing Report"));
        ExcelCommand = new RelayCommand(Excel);
        ShowCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public RelayCommand ShowCommand { get; }
    public override string Title => "Insulin Management";
    public string Notice => "Insulin is a medicine, not a vaccine. This module records issue and stock only. The software never calculates or recommends an insulin dose – the prescribed dosage must be entered exactly as written by an authorised healthcare professional.";
    public List<string> DoctorNames { get; }
    public ObservableCollection<InsulinProductRow> Products { get; } = new();
    public ObservableCollection<InsulinBatchRow> Batches { get; } = new();
    public ObservableCollection<InsulinBatchRow> IssueBatches { get; } = new();
    public ObservableCollection<InsulinRow> Transactions { get; } = new();
    public string Alerts { get; private set; } = "";

    // issue
    public string FindText { get; set; } = "";
    public PatientRow? Patient { get => _patient; set { Set(ref _patient, value); Raise(nameof(PatientText)); } }
    public string PatientText => Patient is null ? "No patient selected" : $"{Patient.PatientNo} – {Patient.Name} ({Patient.GuardianName})  {Patient.Mobile}  {Patient.Cnic}";
    public InsulinProductRow? Product
    {
        get => _product;
        set
        {
            Set(ref _product, value);
            IssueBatches.Clear();
            if (value is not null) foreach (var b in InsulinService.Batches(value.Id, usableOnly: true)) IssueBatches.Add(b);
            Batch = IssueBatches.FirstOrDefault();
            Price = value?.UnitPrice.ToString("0.##") ?? "";
        }
    }
    public InsulinBatchRow? Batch { get => _batch; set => Set(ref _batch, value); }
    public string Vials { get => _vials; set { Set(ref _vials, value); Raise(nameof(Total)); } }
    public string Price { get => _price; set { Set(ref _price, value); Raise(nameof(Total)); } }
    public string Total => "Total: " + Fmt.Money((Ui.ParseInt(Vials) ?? 0) * (Ui.ParseDecimal(Price) ?? 0));
    public DateTime? IssueDate { get; set; } = DateTime.Today;
    public string Doctor { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Remarks { get; set; } = "";

    // products / stock
    public InsulinProductRow? EditProduct { get; set; }
    public InsulinProductRow? SelectedProduct
    {
        get => EditProduct;
        set
        {
            EditProduct = value;
            if (value is not null) { PBrand = value.Brand; PType = value.InsulinType; PStrength = value.Strength; PVial = value.VialSize; PPrice = value.UnitPrice.ToString("0.##"); PLow = value.LowStockLevel.ToString(); PStorage = value.Storage ?? ""; }
            RaiseAll();
        }
    }
    public string PBrand { get; set; } = "";
    public string PType { get; set; } = "";
    public string PStrength { get; set; } = "";
    public string PVial { get; set; } = "";
    public string PPrice { get; set; } = "";
    public string PLow { get; set; } = "10";
    public string PStorage { get; set; } = "";
    public InsulinProductRow? StockProduct { get; set; }
    public string SBatch { get; set; } = "";
    public DateTime? SMfg { get; set; }
    public DateTime? SExpiry { get; set; } = DateTime.Today.AddYears(1);
    public string SVials { get; set; } = "";
    public string SLocation { get; set; } = "";
    public string STemp { get; set; } = "";
    public InsulinBatchRow? SelectedBatch { get; set; }

    // history
    public DateTime? From { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime? To { get; set; } = DateTime.Today;
    public InsulinRow? SelectedTx { get; set; }

    public RelayCommand FindCommand { get; }
    public RelayCommand NewPatientCommand { get; }
    public RelayCommand IssueCommand { get; }
    public RelayCommand SaveProductCommand { get; }
    public RelayCommand NewProductCommand { get; }
    public RelayCommand StockInCommand { get; }
    public RelayCommand WastageCommand { get; }
    public RelayCommand ReceiptCommand { get; }
    public RelayCommand StockReportCommand { get; }
    public RelayCommand DailyCommand { get; }
    public RelayCommand MonthlyCommand { get; }
    public RelayCommand ExcelCommand { get; }

    public override void Refresh()
    {
        var selected = Product?.Id;
        Products.Clear();
        foreach (var p in InsulinService.Products()) Products.Add(p);
        Batches.Clear();
        foreach (var b in InsulinService.Batches()) Batches.Add(b);
        Transactions.Clear();
        foreach (var t in InsulinService.Transactions(Ui.ToDateOnly(From), Ui.ToDateOnly(To))) Transactions.Add(t);
        Product = Products.FirstOrDefault(p => p.Id == selected) ?? Products.FirstOrDefault();
        var alerts = new List<string>();
        alerts.AddRange(Products.Where(p => p.Low).Select(p => $"LOW STOCK: {p.Display} – {p.Available} vial(s) left"));
        alerts.AddRange(Batches.Where(b => b.Available > 0 && b.Status is "EXPIRED" or "EXPIRING SOON").Select(b => $"{b.Status}: {b.Product} batch {b.BatchNumber} ({b.ExpiryText}) – {b.Available} vial(s)"));
        Alerts = string.Join("\n", alerts);
        Raise(nameof(Alerts));
    }

    private void Find()
    {
        var found = PatientService.Search(FindText);
        if (found.Count == 0) { Ui.Info("No patient found. Use 'New Patient' to register the patient first."); return; }
        if (found.Count == 1 || Ui.Confirm($"{found.Count} patients found. Use the first one?\n\n{found[0].Display}")) Patient = found[0];
    }

    private void Issue()
    {
        if (Patient is null) throw new UserException("Search and select the patient first.");
        if (Batch is null) throw new UserException("Select an insulin product with available stock.");
        var id = InsulinService.Issue(new InsulinIssueInput
        {
            PatientId = Patient.Id, BatchId = Batch.Id, Vials = Ui.ParseInt(Vials) ?? 0, UnitPrice = Ui.ParseDecimal(Price) ?? 0,
            IssueDate = Ui.ToDateOnly(IssueDate) ?? Fmt.Today, PrescribingDoctor = Doctor, PrescribedDosage = Dosage, Remarks = Remarks
        });
        Refresh();
        if (Ui.Confirm("Insulin issued and stock updated. Print the receipt?")) PrintReceipt(id);
        Dosage = Remarks = "";
        Vials = "1";
        RaiseAll();
    }

    private void PrintReceipt(long id)
    {
        var r = InsulinService.Receipt(id);
        Printer.Show(Pdf.InsulinReceipt(SettingsService.Load(), r), $"Insulin-{r.TransactionNo}", Patient?.Id, "Insulin Distribution Receipt");
    }

    private void SaveProduct()
    {
        InsulinService.SaveProduct(EditProduct?.Id, PBrand, PType, PStrength, PVial, Ui.ParseDecimal(PPrice) ?? throw new UserException("Enter the unit price."),
            Ui.ParseInt(PLow) ?? 10, PStorage);
        Refresh();
        Ui.Info("Insulin product saved.");
    }

    private void StockIn()
    {
        if (StockProduct is null) throw new UserException("Select the insulin product.");
        if (SExpiry is null) throw new UserException("Enter the expiry date.");
        InsulinService.StockIn(StockProduct.Id, SBatch, Ui.ToDateOnly(SMfg), DateOnly.FromDateTime(SExpiry.Value), Ui.ParseInt(SVials) ?? 0, SLocation, Ui.ParseDecimal(STemp));
        SBatch = SVials = "";
        Refresh();
        RaiseAll();
        Ui.Info("Stock added.");
    }

    private void Wastage()
    {
        if (SelectedBatch is null) throw new UserException("Select a batch in the stock list.");
        var qty = Dialogs.AskText("Wastage / disposal", $"Vials to remove from batch {SelectedBatch.BatchNumber}:", "1");
        if (qty is null) return;
        var reason = Dialogs.AskText("Reason", "Reason (broken, expired, cold-chain failure ...):");
        if (reason is null) return;
        InsulinService.Wastage(SelectedBatch.Id, Ui.ParseInt(qty) ?? 0, reason);
        Refresh();
    }

    private void Report(DateOnly from, DateOnly to, string title)
    {
        var r = ReportService.Period(from, to, title);
        Printer.Show(Pdf.PeriodReport(SettingsService.Load(), r), title.Replace(' ', '-'), null, title);
    }

    private void Excel()
    {
        var path = Ui.SaveExcelDialog("Insulin-Transactions");
        if (path is null) return;
        Reporting.Excel.Export<InsulinRow>(path, "Insulin Dispensing", Transactions,
            ("Receipt", r => r.TransactionNo), ("Date", r => r.DateText), ("Patient ID", r => r.PatientNo), ("Patient", r => r.PatientName),
            ("Insulin", r => r.Product), ("Batch", r => r.Batch), ("Vials", r => r.Vials), ("Unit price", r => r.UnitPrice), ("Total", r => r.Total),
            ("Doctor", r => r.Doctor), ("Prescribed dosage", r => r.Dosage));
        Ui.Info("Saved: " + path);
    }
}

// ============================================================================ Vaccine inventory

public sealed class InventoryViewModel : PageViewModel
{
    private BatchRow? _selected;
    public InventoryViewModel(ShellViewModel shell)
    {
        Vaccines = VaccinationService.Vaccines();
        NewVaccine = Vaccines.FirstOrDefault();
        StockInCommand = new RelayCommand(StockIn, () => Session.Can(Module.Inventory));
        StockOutCommand = new RelayCommand(() => Remove(InventoryTransactionType.StockOut), () => Selected is not null && Session.Can(Module.Inventory));
        WastageCommand = new RelayCommand(() => Remove(InventoryTransactionType.Wastage), () => Selected is not null && Session.Can(Module.Inventory));
        ExpiredCommand = new RelayCommand(() => Remove(InventoryTransactionType.Expired), () => Selected is not null && Session.Can(Module.Inventory));
        QuarantineCommand = new RelayCommand(Quarantine, () => Selected is not null && Session.Can(Module.Inventory));
        VvmCommand = new RelayCommand(p => { if (Selected is not null && p is string s && Enum.TryParse<VvmStatus>(s, out var v)) { InventoryService.SetVvm(Selected.Id, v); Refresh(); } });
        TempCommand = new RelayCommand(AddTemp, () => Session.Can(Module.Inventory));
        ReportCommand = new RelayCommand(() => Printer.Show(Pdf.StockReport(SettingsService.Load(), Batches), "Stock-report", null, "Vaccine Stock Report"));
        ExcelCommand = new RelayCommand(Excel);
        SearchCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Title => "Vaccine Inventory";
    public List<VaccineOption> Vaccines { get; }
    public string Search { get; set; } = "";
    public ObservableCollection<BatchRow> Batches { get; } = new();
    public ObservableCollection<InventoryTxRow> Transactions { get; } = new();
    public ObservableCollection<ColdChainRow> Temperatures { get; } = new();
    public BatchRow? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Alerts { get; private set; } = "";
    public string Totals { get; private set; } = "";

    public VaccineOption? NewVaccine { get; set; }
    public string Brand { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string BatchNumber { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string VialSize { get; set; } = "";
    public DateTime? Expiry { get; set; } = DateTime.Today.AddYears(1);
    public DateTime? Manufactured { get; set; }
    public string Supplier { get; set; } = "";
    public DateTime? Purchased { get; set; } = DateTime.Today;
    public string Storage { get; set; } = "";
    public VvmStatus Vvm { get; set; } = VvmStatus.Stage1;
    public string Location { get; set; } = "";
    public string LowLevel { get; set; } = "10";
    public string TempLocation { get; set; } = "Main refrigerator";
    public string TempValue { get; set; } = "";
    public string TempRemarks { get; set; } = "";

    public RelayCommand StockInCommand { get; }
    public RelayCommand StockOutCommand { get; }
    public RelayCommand WastageCommand { get; }
    public RelayCommand ExpiredCommand { get; }
    public RelayCommand QuarantineCommand { get; }
    public RelayCommand VvmCommand { get; }
    public RelayCommand TempCommand { get; }
    public RelayCommand ReportCommand { get; }
    public RelayCommand ExcelCommand { get; }
    public RelayCommand SearchCommand { get; }

    public override void Refresh()
    {
        Batches.Clear();
        foreach (var b in InventoryService.Batches(Search)) Batches.Add(b);
        Transactions.Clear();
        foreach (var t in InventoryService.Transactions(Fmt.Today.AddDays(-90), Fmt.Today)) Transactions.Add(t);
        Temperatures.Clear();
        foreach (var t in InventoryService.Temperatures()) Temperatures.Add(t);
        var alerts = Batches.Where(b => b.Status is "EXPIRED" or "QUARANTINED" or "VVM DISCARD" or "EXPIRING SOON" or "LOW STOCK" && (b.Available > 0 || b.Status == "LOW STOCK"))
            .Select(b => $"{b.Status}: {b.Vaccine} batch {b.BatchNumber} – {b.Available} dose(s), expiry {b.ExpiryText}").ToList();
        alerts.AddRange(Temperatures.Where(t => t.Excursion && t.At >= DateTime.Today.AddDays(-7)).Take(5)
            .Select(t => $"TEMPERATURE EXCURSION: {t.Location} {t.TemperatureC} °C at {t.AtText} – check the vaccines (VVM) before use."));
        Alerts = string.Join("\n", alerts);
        Totals = $"{Batches.Count} batch(es)  ·  usable doses: {Batches.Where(b => b.Status is "OK" or "LOW STOCK" or "EXPIRING SOON").Sum(b => b.Available)}";
        Raise(nameof(Alerts));
        Raise(nameof(Totals));
    }

    private void StockIn()
    {
        if (NewVaccine is null) throw new UserException("Select the vaccine.");
        if (Expiry is null) throw new UserException("Enter the expiry date.");
        InventoryService.StockIn(new BatchInput
        {
            VaccineId = NewVaccine.Id, Brand = Brand, Manufacturer = Manufacturer, BatchNumber = BatchNumber, Quantity = Ui.ParseInt(Quantity) ?? 0, VialSize = VialSize,
            ExpiryDate = DateOnly.FromDateTime(Expiry.Value), ManufacturingDate = Ui.ToDateOnly(Manufactured), Supplier = Supplier, PurchaseDate = Ui.ToDateOnly(Purchased),
            StorageTemperature = Storage, Vvm = Vvm, StockLocation = Location, LowStockLevel = Ui.ParseInt(LowLevel) ?? 10
        });
        BatchNumber = Quantity = "";
        Refresh();
        RaiseAll();
        Ui.Info("Stock received and saved.");
    }

    private void Remove(InventoryTransactionType type)
    {
        if (Selected is null) return;
        var qty = Dialogs.AskText(type.ToString(), $"{Selected.Vaccine} batch {Selected.BatchNumber} – doses to remove (available {Selected.Available}):",
            type == InventoryTransactionType.Expired ? Selected.Available.ToString() : "1");
        if (qty is null) return;
        var reason = Dialogs.AskText("Reason", type == InventoryTransactionType.Wastage ? "Reason (opened vial discarded, broken, VVM, cold chain ...):" : "Reason / destination:",
            type == InventoryTransactionType.Expired ? "Expired stock disposed" : "");
        if (reason is null) return;
        InventoryService.Remove(Selected.Id, Ui.ParseInt(qty) ?? 0, type, reason);
        Refresh();
    }

    private void Quarantine()
    {
        if (Selected is null) return;
        if (Selected.Quarantined)
        {
            if (Ui.Confirm("Release this batch from quarantine?")) InventoryService.SetQuarantine(Selected.Id, false, null);
        }
        else
        {
            var reason = Dialogs.AskText("Quarantine", "Reason (temperature excursion, recall, damaged ...):");
            if (reason is null) return;
            InventoryService.SetQuarantine(Selected.Id, true, reason);
        }
        Refresh();
    }

    private void AddTemp()
    {
        var t = Ui.ParseDecimal(TempValue) ?? throw new UserException("Enter the temperature in °C, e.g. 4.5");
        InventoryService.AddTemperature(TempLocation, t, TempRemarks);
        if (t < 2m || t > 8m) Ui.Warn("TEMPERATURE EXCURSION: outside +2 °C to +8 °C. Check the VVM of the vaccines and quarantine affected batches if needed.");
        TempValue = TempRemarks = "";
        Refresh();
        RaiseAll();
    }

    private void Excel()
    {
        var path = Ui.SaveExcelDialog("Vaccine-Stock");
        if (path is null) return;
        Reporting.Excel.Export<BatchRow>(path, "Vaccine Stock", Batches,
            ("Vaccine", b => b.Vaccine), ("Brand", b => b.Brand), ("Manufacturer", b => b.Manufacturer), ("Batch", b => b.BatchNumber),
            ("Received", b => b.Received), ("Available", b => b.Available), ("Vial size", b => b.VialSize), ("Expiry", b => b.ExpiryText),
            ("Manufactured", b => b.ManufacturedText), ("Supplier", b => b.Supplier), ("Purchased", b => b.PurchasedText), ("Storage", b => b.Storage),
            ("VVM", b => b.Vvm.ToString()), ("Location", b => b.Location), ("Status", b => b.Status));
        Ui.Info("Saved: " + path);
    }
}

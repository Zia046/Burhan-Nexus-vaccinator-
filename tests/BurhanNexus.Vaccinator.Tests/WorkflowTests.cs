using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using BurhanNexus.Vaccinator.Reporting;

namespace BurhanNexus.Vaccinator.Tests;

/// <summary>End-to-end checks on a real SQLite database in a temporary folder.</summary>
public class WorkflowTests : IDisposable
{
    private readonly string _root;

    public WorkflowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bnv-test-" + Guid.NewGuid().ToString("N"));
        AppPaths.UseRoot(_root);
        DbFactory.Initialize();
        UserService.FirstSetup("Test Center", "Admin", "admin", "secret1");
        UserService.Login("admin", "secret1");
    }

    public void Dispose()
    {
        Session.SignOut();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static int Vaccine(string code)
    {
        using var db = DbFactory.Create();
        return db.Vaccines.Single(v => v.Code == code).Id;
    }

    private static int Protocol(string code, string nameStart)
    {
        using var db = DbFactory.Create();
        return db.VaccineProtocols.Single(p => p.Vaccine!.Code == code && p.Name.StartsWith(nameStart)).Id;
    }

    private static int AddBatch(string code, int qty, DateOnly? expiry = null)
    {
        InventoryService.StockIn(new BatchInput { VaccineId = Vaccine(code), BatchNumber = "B-" + Guid.NewGuid().ToString("N")[..6], Quantity = qty, ExpiryDate = expiry ?? Fmt.Today.AddYears(1) });
        using var db = DbFactory.Create();
        return db.VaccineInventory.OrderByDescending(b => b.Id).First().Id;
    }

    [Fact]
    public void Register_patient_generates_id_saves_new_doctor_and_blocks_duplicate_cnic()
    {
        var id = PatientService.Register(new PatientInput { Name = "Ahmad", GuardianName = "Karim", AgeYears = 30, Cnic = "1510112345671", Mobile = "0300 1234567", AdvisedBy = "Dr. Salma" });
        var row = PatientService.Search("Ahmad").Single();
        Assert.Equal($"BNV-{id:D6}", row.PatientNo);
        Assert.Equal("15101-1234567-1", row.Cnic);
        Assert.Equal("03001234567", row.Mobile);
        Assert.Contains("Dr. Salma", DoctorService.Names());

        Assert.Throws<UserException>(() => PatientService.Register(new PatientInput { Name = "Other", AgeYears = 20, Cnic = "15101-1234567-1" }));
        Assert.Throws<PossibleDuplicateException>(() => PatientService.Register(new PatientInput { Name = "Ahmad", GuardianName = "Karim", AgeYears = 30 }));

        PatientService.Register(new PatientInput { Name = "Bilal", AgeYears = 5, AdvisedBy = "salma" });
        Assert.Single(DoctorService.List(), d => d.Name == "Dr. Salma");
        Assert.Single(PatientService.Search("03001234567"));
        Assert.Single(PatientService.Search("15101-1234567-1"));
    }

    [Fact]
    public void Newborn_registration_creates_full_epi_schedule()
    {
        var dob = Fmt.Today.AddDays(-50);
        var id = PatientService.RegisterNewborn(new NewbornInput { FatherName = "Imran", MotherName = "Ayesha", Gender = Gender.Female, DateOfBirth = dob });
        var doses = ScheduleService.DosesForPatient(id);
        // BCG 1 + OPV 4 + IPV 2 + Penta 3 + PCV 3 + Rota 2 + MR 2 + TCV 1 + HepB-0 1 = 19
        Assert.Equal(19, doses.Count);
        Assert.Contains(doses, d => d.DoseLabel == "Penta-1" && d.DueDate == dob.AddDays(42));
        Assert.Contains(doses, d => d.DoseLabel == "MR-2" && d.DueDate == dob.AddMonths(15));
        Assert.Contains(doses, d => d.DoseLabel == "BCG" && d.Status == "Overdue");
        Assert.Throws<UserException>(() => PatientService.RegisterNewborn(new NewbornInput { FatherName = "Imran", MotherName = "Ayesha", Gender = Gender.Female, DateOfBirth = dob }));
    }

    [Fact]
    public void Administering_first_dose_creates_schedule_deducts_stock_and_logs()
    {
        var pid = PatientService.Register(new PatientInput { Name = "Sara", AgeYears = 25, Gender = Gender.Female });
        var batch = AddBatch("HEPB", 10);
        var result = VaccinationService.Administer(new AdministrationInput
        {
            PatientId = pid, VaccineId = Vaccine("HEPB"), ProtocolId = Protocol("HEPB", "Hep B 0-1-6"), DoseNumber = 1, Date = Fmt.Today,
            BatchId = batch, AdministeredBy = "Nurse", Route = AdministrationRoute.IM
        });
        Assert.Equal(2, result.RemainingDoses);
        Assert.Equal(Fmt.Today.AddMonths(1), result.NextDueDate);

        using (var db = DbFactory.Create())
        {
            Assert.Equal(9, db.VaccineInventory.Find(batch)!.QuantityAvailable);
            Assert.Single(db.InventoryTransactions.Where(t => t.Type == InventoryTransactionType.Administered));
            Assert.Contains(db.AuditLogs, a => a.Action == "Administer");
        }

        // Second dose found on the existing course, not a duplicate course
        var next = ScheduleService.Appointments(AppointmentFilter.AllUpcoming).Single(a => a.PatientId == pid && a.DoseNumber == 2);
        var defaults = VaccinationService.DefaultsForAppointment(next.AppointmentId);
        Assert.Equal(2, defaults.DoseNumber);
        Assert.Throws<ConfirmationNeededException>(() => VaccinationService.Administer(new AdministrationInput
        {
            PatientId = pid, AppointmentId = next.AppointmentId, VaccineId = defaults.VaccineId, DoseNumber = 2, Date = Fmt.Today, BatchId = batch, AdministeredBy = "Nurse"
        }));
    }

    [Fact]
    public void Expired_batch_cannot_be_received_or_used_and_historical_entry_needs_no_stock()
    {
        Assert.Throws<UserException>(() => AddBatch("TCV", 5, Fmt.Today.AddDays(-1)));
        var pid = PatientService.Register(new PatientInput { Name = "Old Card", AgeYears = 10 });
        var r = VaccinationService.Administer(new AdministrationInput
        {
            PatientId = pid, VaccineId = Vaccine("TCV"), ProtocolId = Protocol("TCV", "TCV single"), DoseNumber = 1, Date = Fmt.Today.AddYears(-1),
            Historical = true, BatchNumber = "OLD123", AdministeredBy = "Previous center"
        });
        Assert.Equal(0, r.RemainingDoses);
        Assert.True(VaccinationService.History(null, null, "Old Card").Single().Historical);
    }

    [Fact]
    public void Anti_rabies_case_generates_schedule_and_lists()
    {
        var input = new RabiesInput
        {
            NewPatient = new PatientInput { Name = "Bite Patient", AgeYears = 40 },
            BiteDate = Fmt.Today, FirstVaccineDate = Fmt.Today, Category = BiteCategory.III, ProtocolId = Protocol("RABIES", "PEP IM 1-site – days 0, 3, 7, 14"),
            WoundWashed = true
        };
        Assert.Contains(RabiesService.Warnings(input), w => w.Contains("CATEGORY III"));
        var (caseId, pid) = RabiesService.Register(input);
        var doses = ScheduleService.DosesForPatient(pid);
        Assert.Equal(new[] { 0, 3, 7, 14 }.Select(d => Fmt.Today.AddDays(d)), doses.OrderBy(d => d.DoseNumber).Select(d => d.DueDate));
        Assert.Single(RabiesService.Cases(RabiesList.FirstDosePending));
        Assert.Single(RabiesService.Cases(RabiesList.DueToday));

        var batch = AddBatch("RABIES", 4);
        var appt = RabiesService.NextAppointment(caseId)!.Value;
        VaccinationService.Administer(new AdministrationInput { PatientId = pid, AppointmentId = appt, VaccineId = Vaccine("RABIES"), DoseNumber = 1, BatchId = batch, AdministeredBy = "Nurse" });
        Assert.Single(RabiesService.Cases(RabiesList.SecondDosePending));

        var profile = PatientService.Profile(pid);
        Assert.NotEmpty(Pdf.AntiRabiesCard(SettingsService.Load(), profile, RabiesService.Load(caseId)));
    }

    [Fact]
    public void Insulin_issue_updates_stock_history_and_receipt()
    {
        InsulinService.SaveProduct(null, "Brand X", "Regular", "100 IU/mL", "10 mL", 1200m, 2, null);
        var product = InsulinService.Products().Single();
        InsulinService.StockIn(product.Id, "INS1", null, Fmt.Today.AddMonths(10), 5, "Fridge", 5m);
        var pid = PatientService.Register(new PatientInput { Name = "Diabetic", AgeYears = 55 });
        var batch = InsulinService.Batches(product.Id, true).Single();
        var tx = InsulinService.Issue(new InsulinIssueInput { PatientId = pid, BatchId = batch.Id, Vials = 2, UnitPrice = 1200m, PrescribingDoctor = "Dr. Hamid", PrescribedDosage = "As written by prescriber" });
        Assert.Equal(3, InsulinService.Batches(product.Id).Single().Available);
        Assert.Throws<UserException>(() => InsulinService.Issue(new InsulinIssueInput { PatientId = pid, BatchId = batch.Id, Vials = 9, UnitPrice = 1 }));
        var receipt = InsulinService.Receipt(tx);
        Assert.Equal(2400m, receipt.Total);
        Assert.NotEmpty(Pdf.InsulinReceipt(SettingsService.Load(), receipt));
        Assert.Single(PatientService.Profile(pid).Insulin);
    }

    [Fact]
    public void Reschedule_and_not_arrived_and_overdue_lists()
    {
        var pid = PatientService.Register(new PatientInput { Name = "Late", AgeYears = 20 });
        ScheduleService.StartCourse(pid, Protocol("HEPB", "Hep B 0-1-6"), Fmt.Today.AddMonths(-3));
        var overdue = ScheduleService.Appointments(AppointmentFilter.Overdue).Where(a => a.PatientId == pid).ToList();
        Assert.Equal(2, overdue.Count); // dose 1 and 2 are past due
        ScheduleService.MarkNotArrived(overdue[0].AppointmentId);
        Assert.Throws<UserException>(() => ScheduleService.Reschedule(overdue[0].AppointmentId, Fmt.Today.AddDays(-1), null));
        ScheduleService.Reschedule(overdue[0].AppointmentId, Fmt.Today.AddDays(2), "Family request");
        Assert.Contains(ScheduleService.Appointments(AppointmentFilter.Next7Days), a => a.AppointmentId == overdue[0].AppointmentId);

        var data = DashboardService.Load();
        Assert.True(data.TotalPatients >= 1);
        Assert.True(data.OverdueVaccinations >= 1);
    }

    [Fact]
    public void Every_document_and_excel_export_is_generated()
    {
        var pid = PatientService.RegisterNewborn(new NewbornInput { FatherName = "Print", Gender = Gender.Male, DateOfBirth = Fmt.Today.AddDays(-3) });
        var p = PatientService.Profile(pid);
        var c = SettingsService.Load();
        Assert.NotEmpty(Pdf.RegistrationForm(c, p));
        Assert.NotEmpty(Pdf.VaccinationCard(c, p));
        Assert.NotEmpty(Pdf.NewbornCard(c, p));
        Assert.NotEmpty(Pdf.UpcomingSchedule(c, p));
        Assert.NotEmpty(Pdf.PatientHistory(c, p));
        Assert.NotEmpty(Pdf.PeriodReport(c, ReportService.Period(Fmt.Today.AddDays(-30), Fmt.Today, "Monthly Vaccination Report")));
        Assert.NotEmpty(Pdf.StockReport(c, InventoryService.Batches()));
        Assert.NotEmpty(Pdf.AppointmentList(c, "Upcoming", ScheduleService.Appointments(AppointmentFilter.AllUpcoming)));
        Assert.NotEmpty(Pdf.SchedulePreview(c, "Rabies", "IM", null, ScheduleService.Preview(Protocol("RABIES", "PEP IM 1-site – days 0, 3, 7, 14"), Fmt.Today), "WHO"));
        var xlsx = Path.Combine(_root, "test.xlsx");
        Excel.Export<AppointmentRow>(xlsx, "Upcoming", ScheduleService.Appointments(AppointmentFilter.AllUpcoming), ("Patient", a => a.PatientName), ("Days", a => a.DaysOverdue));
        Assert.True(File.Exists(xlsx));
    }

    [Fact]
    public void Roles_limit_what_users_can_do()
    {
        UserService.Save(null, "recep", "Reception", UserRole.Receptionist, true, "secret2");
        UserService.Login("recep", "secret2");
        Assert.Throws<UserException>(() => InventoryService.StockIn(new BatchInput { VaccineId = Vaccine("BCG"), BatchNumber = "X", Quantity = 1 }));
        Assert.Throws<UserException>(() => VaccinationService.Administer(new AdministrationInput { PatientId = 1, VaccineId = 1, AdministeredBy = "x" }));
        UserService.Login("admin", "secret1");
        Assert.Throws<UserException>(() => UserService.Save(1, "admin", "Admin", UserRole.Vaccinator, true, null)); // last administrator
    }

    [Fact]
    public void Backup_creates_a_valid_copy()
    {
        var file = Path.Combine(_root, "backup.db");
        BackupService.Create(file);
        Assert.True(BackupService.LooksLikeVaccinatorDatabase(file));
    }
}

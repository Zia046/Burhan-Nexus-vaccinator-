using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

public sealed record ChartPoint(string Label, int Value);

public sealed class DashboardData
{
    public int TotalPatients { get; init; }
    public int TotalNewborns { get; init; }
    public int TotalVaccinations { get; init; }
    public int TodaysVaccinations { get; init; }
    public int UpcomingVaccinations { get; init; }
    public int OverdueVaccinations { get; init; }
    public int CompletedRabiesCourses { get; init; }
    public int ActiveRabiesPatients { get; init; }
    public int InsulinVialsIssued { get; init; }
    public int AvailableVaccineStock { get; init; }
    public List<AppointmentRow> TodaysAppointments { get; init; } = new();
    public List<AppointmentRow> UpcomingAppointments { get; init; } = new();
    public List<AppointmentRow> OverduePatients { get; init; } = new();
    public List<PatientRow> RecentPatients { get; init; } = new();
    public List<VaccinationRow> RecentVaccinations { get; init; } = new();
    public List<BatchRow> LowStock { get; init; } = new();
    public List<BatchRow> Expiring { get; init; } = new();
    public List<InsulinRow> RecentInsulin { get; init; } = new();
    public List<ChartPoint> Daily { get; init; } = new();
    public List<ChartPoint> Monthly { get; init; } = new();
    public List<ChartPoint> ByVaccine { get; init; } = new();
    public List<ChartPoint> ByAge { get; init; } = new();
    public List<ChartPoint> Rabies { get; init; } = new();
    public List<ColdChainRow> Excursions { get; init; } = new();
}

public static class DashboardService
{
    /// <summary>Every figure is counted from the database records.</summary>
    public static DashboardData Load()
    {
        using var db = DbFactory.Create();
        var today = Fmt.Today;
        var activePending = db.Appointments.Where(a => a.Status == AppointmentStatus.Pending && a.Course!.Status == CourseStatus.Active);

        var rabiesCourses = db.PatientCourses.Where(c => c.AntiRabiesCaseId != null).Select(c => c.Status).ToList();
        var batches = db.VaccineInventory.AsNoTracking().Include(b => b.Vaccine).ToList();

        var since = today.AddDays(-13);
        var yearStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var recent = db.VaccinationRecords.AsNoTracking().Where(r => r.AdministrationDate >= yearStart)
            .Select(r => new { r.AdministrationDate, r.VaccineId, r.PatientId }).ToList();

        var vaccineNames = db.Vaccines.ToDictionary(v => v.Id, v => v.Name);
        var pids = recent.Select(r => r.PatientId).Distinct().ToList();
        var dobs = db.Patients.Where(p => pids.Contains(p.Id)).Select(p => new { p.Id, p.DateOfBirth, p.AgeYears }).ToList()
            .ToDictionary(p => p.Id);

        string AgeGroup(int patientId, DateOnly on)
        {
            if (!dobs.TryGetValue(patientId, out var p)) return "Unknown";
            int months;
            if (p.DateOfBirth is DateOnly d) months = DateMath.Age(d, on).Months;
            else if (p.AgeYears is int y) months = y * 12;
            else return "Unknown";
            return months switch { < 12 => "0–1 yr", < 60 => "1–5 yrs", < 216 => "5–18 yrs", < 720 => "18–60 yrs", _ => "60+ yrs" };
        }

        var rabiesCases = db.AntiRabiesPatients.Select(c => c.Category).ToList();

        return new DashboardData
        {
            TotalPatients = db.Patients.Count(p => !p.IsArchived),
            TotalNewborns = db.Patients.Count(p => p.Category == PatientCategory.Newborn),
            TotalVaccinations = db.VaccinationRecords.Count(),
            TodaysVaccinations = db.VaccinationRecords.Count(r => r.AdministrationDate == today),
            UpcomingVaccinations = activePending.Count(a => a.DueDate >= today),
            OverdueVaccinations = activePending.Count(a => a.DueDate < today),
            CompletedRabiesCourses = rabiesCourses.Count(s => s == CourseStatus.Completed),
            ActiveRabiesPatients = rabiesCourses.Count(s => s == CourseStatus.Active),
            InsulinVialsIssued = -db.InsulinTransactions.Where(t => t.Type == InsulinTransactionType.Issue).Sum(t => (int?)t.Quantity) ?? 0,
            AvailableVaccineStock = batches.Where(b => b.WhyNotUsable(today) is null).Sum(b => b.QuantityAvailable),
            TodaysAppointments = ScheduleService.Appointments(AppointmentFilter.Today).Take(200).ToList(),
            UpcomingAppointments = ScheduleService.Appointments(AppointmentFilter.Next7Days).Where(a => a.DueDate > today).Take(200).ToList(),
            OverduePatients = ScheduleService.Appointments(AppointmentFilter.Overdue).OrderByDescending(a => a.DaysOverdue).Take(200).ToList(),
            RecentPatients = PatientService.Search(null, max: 15),
            RecentVaccinations = VaccinationService.History(db).Take(15).ToList(),
            LowStock = InventoryService.Batches().Where(b => b.Status is "LOW STOCK" or "OUT OF STOCK").ToList(),
            Expiring = InventoryService.Batches().Where(b => b.Status is "EXPIRING SOON" or "EXPIRED" && b.Available > 0).ToList(),
            RecentInsulin = InsulinService.Transactions(db).Take(15).ToList(),
            Daily = Enumerable.Range(0, 14).Select(i => since.AddDays(i))
                .Select(d => new ChartPoint(d.ToString("dd MMM"), recent.Count(r => r.AdministrationDate == d))).ToList(),
            Monthly = Enumerable.Range(0, 12).Select(i => yearStart.AddMonths(i))
                .Select(m => new ChartPoint(m.ToString("MMM yy"), recent.Count(r => r.AdministrationDate.Year == m.Year && r.AdministrationDate.Month == m.Month))).ToList(),
            ByVaccine = recent.GroupBy(r => r.VaccineId).Select(g => new ChartPoint(vaccineNames.TryGetValue(g.Key, out var n) ? n : "?", g.Count()))
                .OrderByDescending(p => p.Value).Take(12).ToList(),
            ByAge = new[] { "0–1 yr", "1–5 yrs", "5–18 yrs", "18–60 yrs", "60+ yrs", "Unknown" }
                .Select(g => new ChartPoint(g, recent.Count(r => AgeGroup(r.PatientId, r.AdministrationDate) == g))).Where(p => p.Label != "Unknown" || p.Value > 0).ToList(),
            Rabies = new List<ChartPoint>
            {
                new("Cat I", rabiesCases.Count(c => c == BiteCategory.I)),
                new("Cat II", rabiesCases.Count(c => c == BiteCategory.II)),
                new("Cat III", rabiesCases.Count(c => c == BiteCategory.III)),
                new("Active", rabiesCourses.Count(s => s == CourseStatus.Active)),
                new("Completed", rabiesCourses.Count(s => s == CourseStatus.Completed))
            },
            Excursions = InventoryService.Temperatures(50).Where(t => t.Excursion && t.At >= DateTime.Today.AddDays(-7)).ToList()
        };
    }
}

// ============================================================================ Reports

public sealed class PeriodReport
{
    public string Title { get; init; } = "";
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public List<VaccinationRow> Vaccinations { get; init; } = new();
    public List<ChartPoint> ByVaccine { get; init; } = new();
    public List<InsulinRow> Insulin { get; init; } = new();
    public int NewPatients { get; init; }
    public int NewRabiesCases { get; init; }
    public int Wastage { get; init; }
}

public static class ReportService
{
    public static PeriodReport Period(DateOnly from, DateOnly to, string title)
    {
        if (to < from) throw new UserException("The 'to' date is before the 'from' date.");
        using var db = DbFactory.Create();
        var vacc = VaccinationService.History(db, null, from, to).OrderBy(v => v.Date).ThenBy(v => v.Time).ToList();
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new PeriodReport
        {
            Title = title, From = from, To = to, Vaccinations = vacc,
            ByVaccine = vacc.GroupBy(v => v.Vaccine).Select(g => new ChartPoint(g.Key, g.Count())).OrderByDescending(g => g.Value).ToList(),
            Insulin = InsulinService.Transactions(db, null, from, to),
            NewPatients = db.Patients.Count(p => p.RegistrationDate >= from && p.RegistrationDate <= to),
            NewRabiesCases = db.AntiRabiesPatients.Count(c => c.FirstVaccineDate >= from && c.FirstVaccineDate <= to),
            Wastage = -(db.InventoryTransactions.Where(t => t.Type == InventoryTransactionType.Wastage && t.At >= fromDt && t.At < toDt).Sum(t => (int?)t.Quantity) ?? 0)
        };
    }

    public static List<AuditRow> AuditTrail(int max = 1000)
    {
        using var db = DbFactory.Create();
        return db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(max).ToList()
            .Select(a => new AuditRow(a.At, a.Username, a.Action, a.Entity, a.EntityId, a.Details)).ToList();
    }
}

public sealed record AuditRow(DateTime At, string Username, string Action, string Entity, string? EntityId, string? Details)
{
    public string AtText => Fmt.DT(At);
}

// ============================================================================ Vaccine database management (Settings)

public static class VaccineDatabaseService
{
    public static List<Vaccine> Vaccines()
    {
        using var db = DbFactory.Create();
        return db.Vaccines.AsNoTracking().Include(v => v.Protocols).ThenInclude(p => p.Doses).OrderBy(v => v.Name).ToList();
    }

    public static Vaccine? Vaccine(int id)
    {
        using var db = DbFactory.Create();
        return db.Vaccines.AsNoTracking().Include(v => v.Protocols).ThenInclude(p => p.Doses).FirstOrDefault(v => v.Id == id);
    }

    public static int SaveVaccine(Vaccine input)
    {
        Session.Require(Module.VaccineDatabase);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code)) throw new UserException("Enter the vaccine code and name.");
        using var db = DbFactory.Create();
        var code = input.Code.Trim().ToUpperInvariant();
        if (db.Vaccines.Any(v => v.Code == code && v.Id != input.Id)) throw new UserException("Another vaccine already uses this code.");
        var v = input.Id == 0 ? new Vaccine() : db.Vaccines.Find(input.Id) ?? throw new UserException("Vaccine not found.");
        if (input.Id == 0) db.Vaccines.Add(v);
        v.Code = code; v.Name = input.Name.Trim(); v.FullName = input.FullName?.Trim() ?? ""; v.DiseasePrevented = input.DiseasePrevented?.Trim() ?? "";
        v.RecommendedAge = Validation.Clean(input.RecommendedAge); v.DoseVolume = Validation.Clean(input.DoseVolume); v.Route = input.Route;
        v.InjectionSite = Validation.Clean(input.InjectionSite); v.MinimumAge = Validation.Clean(input.MinimumAge); v.BoosterRequirements = Validation.Clean(input.BoosterRequirements);
        v.StorageTemperature = Validation.Clean(input.StorageTemperature); v.SpecialInstructions = Validation.Clean(input.SpecialInstructions);
        v.Reference = Validation.Clean(input.Reference); v.IsActive = input.IsActive; v.LastUpdated = Fmt.Today;
        Audit.Add(db, input.Id == 0 ? "Add" : "Update", "Vaccines", code);
        db.SaveChanges();
        return v.Id;
    }

    public static VaccineProtocol? Protocol(int id)
    {
        using var db = DbFactory.Create();
        return db.VaccineProtocols.AsNoTracking().Include(p => p.Doses).Include(p => p.Vaccine).FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Saves a protocol and its doses. Changing a protocol does not change courses already started; only new courses use the new doses.</summary>
    public static int SaveProtocol(VaccineProtocol input, List<ProtocolDose> doses)
    {
        Session.Require(Module.VaccineDatabase);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new UserException("Enter the protocol name.");
        if (doses.Count == 0) throw new UserException("Add at least one dose.");
        var numbers = doses.Select(d => d.DoseNumber).ToList();
        if (numbers.Distinct().Count() != numbers.Count || numbers.Any(n => n <= 0)) throw new UserException("Dose numbers must be unique and greater than zero.");
        if (doses.Any(d => d.OffsetValue < 0 || d.MinIntervalValue < 0)) throw new UserException("Offsets and intervals cannot be negative.");
        if (doses.Any(d => string.IsNullOrWhiteSpace(d.Label))) throw new UserException("Every dose needs a label (for example 'Dose 1' or 'Day 3').");

        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var p = input.Id == 0 ? new VaccineProtocol() : db.VaccineProtocols.Include(x => x.Doses).First(x => x.Id == input.Id);
        if (input.Id == 0) { db.VaccineProtocols.Add(p); p.SortOrder = (db.VaccineProtocols.Max(x => (int?)x.SortOrder) ?? 0) + 1; }
        var changed = input.Id != 0 && (p.Doses.Count != doses.Count || p.Anchor != input.Anchor);
        p.VaccineId = input.VaccineId; p.Name = input.Name.Trim(); p.TargetGroup = input.TargetGroup?.Trim() ?? ""; p.Kind = input.Kind; p.Anchor = input.Anchor;
        p.Description = Validation.Clean(input.Description); p.Warnings = Validation.Clean(input.Warnings); p.CatchUpRule = Validation.Clean(input.CatchUpRule);
        p.ReviewIfLateByDays = input.ReviewIfLateByDays; p.Reference = Validation.Clean(input.Reference); p.IsActive = input.IsActive;

        // Doses used by existing appointments are updated in place, never deleted.
        var used = db.Appointments.Where(a => a.ProtocolDose!.ProtocolId == p.Id).Select(a => a.ProtocolDoseId).Distinct().ToList();
        foreach (var old in p.Doses.ToList())
        {
            var match = doses.FirstOrDefault(d => d.DoseNumber == old.DoseNumber);
            if (match is null)
            {
                if (used.Contains(old.Id)) throw new UserException($"Dose {old.DoseNumber} is used by patients' schedules and cannot be removed. Create a new protocol instead.");
                db.VaccineDoses.Remove(old);
                changed = true;
                continue;
            }
            if (old.OffsetValue != match.OffsetValue || old.OffsetUnit != match.OffsetUnit || old.MinIntervalValue != match.MinIntervalValue || old.MinIntervalUnit != match.MinIntervalUnit)
                changed = true;
            Copy(match, old);
        }
        foreach (var n in doses.Where(d => p.Doses.All(o => o.DoseNumber != d.DoseNumber)))
        {
            var d = new ProtocolDose();
            Copy(n, d);
            p.Doses.Add(d);
            changed = true;
        }

        // Any schedule change needs a fresh approval by the center.
        if (input.Id == 0 || changed) { p.ApprovedByCenter = false; p.ApprovedBy = null; }
        Audit.Add(db, input.Id == 0 ? "Add" : "Update", "VaccineProtocols", p.Name, changed ? "Schedule changed – approval reset" : null);
        db.SaveChanges();
        tx.Commit();
        return p.Id;
    }

    private static void Copy(ProtocolDose from, ProtocolDose to)
    {
        to.DoseNumber = from.DoseNumber; to.Label = from.Label.Trim(); to.OffsetValue = from.OffsetValue; to.OffsetUnit = from.OffsetUnit;
        to.MinIntervalValue = from.MinIntervalValue; to.MinIntervalUnit = from.MinIntervalValue is null ? null : from.MinIntervalUnit ?? IntervalUnit.Days;
        to.DoseVolume = Validation.Clean(from.DoseVolume); to.Route = from.Route; to.InjectionSite = Validation.Clean(from.InjectionSite); to.IsBooster = from.IsBooster;
    }

    public static void Approve(int protocolId, bool approved)
    {
        Session.Require(Module.VaccineDatabase);
        using var db = DbFactory.Create();
        var p = db.VaccineProtocols.Find(protocolId) ?? throw new UserException("Protocol not found.");
        p.ApprovedByCenter = approved;
        p.ApprovedBy = approved ? $"{Session.DisplayName} on {Fmt.D(Fmt.Today)}" : null;
        Audit.Add(db, approved ? "Approve" : "Withdraw approval", "VaccineProtocols", p.Name);
        db.SaveChanges();
    }
}

using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

/// <summary>Thrown when the entry is possible but unusual. The UI shows the message and calls again with confirmed = true.</summary>
public sealed class ConfirmationNeededException : Exception
{
    public ConfirmationNeededException(string message) : base(message) { }
}

public sealed record VaccineOption(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record BatchOption(int Id, string BatchNumber, string? Brand, DateOnly Expiry, int Available)
{
    public string Display => $"{BatchNumber}  ·  {Brand}  ·  Exp {Fmt.D(Expiry)}  ·  {Available} left";
    public override string ToString() => Display;
}

public sealed class AdministrationInput
{
    public int PatientId { get; set; }
    public int? AppointmentId { get; set; }
    public int VaccineId { get; set; }
    public int? ProtocolId { get; set; }
    public int DoseNumber { get; set; } = 1;
    public DateOnly Date { get; set; } = Fmt.Today;
    public TimeOnly Time { get; set; } = TimeOnly.FromDateTime(DateTime.Now);
    public string? DoseVolume { get; set; }
    public AdministrationRoute Route { get; set; }
    public string? InjectionSite { get; set; }
    public int? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string AdministeredBy { get; set; } = "";
    public string? Remarks { get; set; }
    /// <summary>A dose given in the past (from the patient's old card): no stock is deducted.</summary>
    public bool Historical { get; set; }
    public int? AntiRabiesCaseId { get; set; }
}

public sealed record AdministrationResult(int RecordId, string RecordNo, int PatientId, string Message, DateOnly? NextDueDate, int RemainingDoses, bool NeedsReview);

public sealed class AdministrationDefaults
{
    public int PatientId { get; init; }
    public string PatientNo { get; init; } = "";
    public string PatientName { get; init; } = "";
    public int? AppointmentId { get; init; }
    public int VaccineId { get; init; }
    public string VaccineName { get; init; } = "";
    public int? ProtocolId { get; init; }
    public string? ProtocolName { get; init; }
    public bool ProtocolApproved { get; init; }
    public int DoseNumber { get; init; }
    public string DoseLabel { get; init; } = "";
    public DateOnly? DueDate { get; init; }
    public string? DoseVolume { get; init; }
    public AdministrationRoute Route { get; init; }
    public string? InjectionSite { get; init; }
    public string? Warnings { get; init; }
    public string? ReviewReason { get; init; }
}

public static class VaccinationService
{
    public static AdministrationDefaults DefaultsForAppointment(int appointmentId)
    {
        using var db = DbFactory.Create();
        var a = db.Appointments.Include(x => x.Patient).Include(x => x.Vaccine).Include(x => x.ProtocolDose)
            .Include(x => x.Course).ThenInclude(c => c!.Protocol).FirstOrDefault(x => x.Id == appointmentId) ?? throw new UserException("Appointment not found.");
        var p = a.Course!.Protocol!;
        return new AdministrationDefaults
        {
            PatientId = a.PatientId, PatientNo = a.Patient!.PatientNo, PatientName = a.Patient.Name, AppointmentId = a.Id, VaccineId = a.VaccineId,
            VaccineName = a.Vaccine!.Name, ProtocolId = p.Id, ProtocolName = p.Name, ProtocolApproved = p.ApprovedByCenter, DoseNumber = a.DoseNumber,
            DoseLabel = a.DoseLabel, DueDate = a.DueDate, DoseVolume = a.ProtocolDose?.DoseVolume ?? a.Vaccine.DoseVolume,
            Route = a.ProtocolDose?.Route ?? a.Vaccine.Route, InjectionSite = a.ProtocolDose?.InjectionSite ?? a.Vaccine.InjectionSite,
            Warnings = p.Warnings, ReviewReason = a.Course.NeedsClinicalReview ? a.Course.ReviewReason : null
        };
    }

    /// <summary>Defaults when a vaccine is chosen from the patient profile: the next pending dose of an active course, otherwise dose 1.</summary>
    public static AdministrationDefaults DefaultsFor(int patientId, int vaccineId, int? protocolId)
    {
        using var db = DbFactory.Create();
        var next = db.Appointments.Include(a => a.Course)
            .Where(a => a.PatientId == patientId && a.VaccineId == vaccineId && a.Status == AppointmentStatus.Pending && a.Course!.Status == CourseStatus.Active
                        && (protocolId == null || a.Course.ProtocolId == protocolId))
            .OrderBy(a => a.DoseNumber).FirstOrDefault();
        if (next is not null) return DefaultsForAppointment(next.Id);

        var patient = db.Patients.Find(patientId) ?? throw new UserException("Patient not found.");
        var vaccine = db.Vaccines.Find(vaccineId) ?? throw new UserException("Vaccine not found.");
        var protocol = protocolId is int pid ? db.VaccineProtocols.Include(x => x.Doses).First(x => x.Id == pid) : null;
        var dose1 = protocol?.Doses.OrderBy(d => d.DoseNumber).FirstOrDefault();
        return new AdministrationDefaults
        {
            PatientId = patientId, PatientNo = patient.PatientNo, PatientName = patient.Name, VaccineId = vaccineId, VaccineName = vaccine.Name,
            ProtocolId = protocol?.Id, ProtocolName = protocol?.Name, ProtocolApproved = protocol?.ApprovedByCenter ?? false, DoseNumber = dose1?.DoseNumber ?? 1,
            DoseLabel = dose1?.Label ?? "Dose 1", DoseVolume = dose1?.DoseVolume ?? vaccine.DoseVolume, Route = dose1?.Route ?? vaccine.Route,
            InjectionSite = dose1?.InjectionSite ?? vaccine.InjectionSite, Warnings = protocol?.Warnings
        };
    }

    public static AdministrationResult Administer(AdministrationInput input, bool confirmed = false)
    {
        Session.Require(Module.Vaccination);
        if (input.Date > Fmt.Today) throw new UserException("The administration date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(input.AdministeredBy)) throw new UserException("Enter who administered the vaccine (Administered By).");

        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();

        var patient = db.Patients.Find(input.PatientId) ?? throw new UserException("Patient not found.");
        var vaccine = db.Vaccines.Find(input.VaccineId) ?? throw new UserException("Vaccine not found.");

        // 1. Find the scheduled dose, or start the course.
        Appointment? appointment = null;
        if (input.AppointmentId is int aid)
        {
            appointment = db.Appointments.Include(a => a.Course).FirstOrDefault(a => a.Id == aid) ?? throw new UserException("Appointment not found.");
            if (appointment.PatientId != patient.Id) throw new UserException("This appointment belongs to another patient.");
            if (appointment.Status != AppointmentStatus.Pending) throw new UserException("This dose has already been recorded.");
        }
        else
        {
            var course = db.PatientCourses.Include(c => c.Appointments)
                .Where(c => c.PatientId == patient.Id && c.Status == CourseStatus.Active && c.Protocol!.VaccineId == vaccine.Id
                            && (input.ProtocolId == null || c.ProtocolId == input.ProtocolId))
                .OrderBy(c => c.Id).FirstOrDefault();
            if (course is null)
            {
                if (input.ProtocolId is not int pid) throw new UserException("Select the protocol (schedule) for this vaccine.");
                var protocol = db.VaccineProtocols.Include(p => p.Doses).First(p => p.Id == pid);
                if (protocol.Anchor == ScheduleAnchor.FirstDose && input.DoseNumber != protocol.Doses.Min(d => d.DoseNumber))
                    throw new UserException("No course of this vaccine exists yet. Record the first dose first (tick 'Old dose from card' for doses given earlier), then the next doses.");
                course = ScheduleService.StartCourse(db, patient.Id, pid, input.Date, input.AntiRabiesCaseId);
                db.SaveChanges();
            }
            appointment = course.Appointments.FirstOrDefault(a => a.DoseNumber == input.DoseNumber && a.Status == AppointmentStatus.Pending)
                          ?? throw new UserException($"Dose {input.DoseNumber} of this course is not pending (already recorded or not part of the protocol).");
            appointment.Course = course;
        }

        if (!confirmed)
        {
            if (input.Date < appointment.CalculatedDate)
                throw new ConfirmationNeededException($"This dose is planned for {Fmt.D(appointment.CalculatedDate)} (protocol interval / minimum age). " +
                                                      $"Recording it on {Fmt.D(input.Date)} is EARLY. Continue only if a healthcare professional has decided so.");
            var earlierPending = db.Appointments.Where(a => a.CourseId == appointment.CourseId && a.DoseNumber < appointment.DoseNumber && a.Status == AppointmentStatus.Pending)
                .Select(a => a.DoseLabel).ToList();
            if (earlierPending.Count > 0)
                throw new ConfirmationNeededException($"Earlier dose(s) {string.Join(", ", earlierPending)} are not recorded yet. Record them first, or continue if they were skipped on professional advice.");
        }

        // 2. Batch and stock.
        VaccineBatch? batch = null;
        if (!input.Historical)
        {
            if (input.BatchId is not int bid) throw new UserException("Select the batch used. Every administered vaccine must be linked to its batch number.");
            batch = db.VaccineInventory.Find(bid) ?? throw new UserException("Batch not found.");
            if (batch.VaccineId != vaccine.Id) throw new UserException("This batch belongs to another vaccine.");
            var why = batch.WhyNotUsable(input.Date);
            if (why is not null) throw new UserException($"This batch cannot be used: {why}.");
            batch.QuantityAvailable -= 1;
        }

        var record = new VaccinationRecord
        {
            RecordNo = "TMP-" + Guid.NewGuid().ToString("N"), PatientId = patient.Id, VaccineId = vaccine.Id, AppointmentId = appointment.Id,
            CourseId = appointment.CourseId, AntiRabiesCaseId = input.AntiRabiesCaseId ?? appointment.Course?.AntiRabiesCaseId,
            DoseNumber = appointment.DoseNumber, DoseLabel = appointment.DoseLabel,
            AdministrationDate = input.Date, AdministrationTime = input.Time, DoseVolume = Validation.Clean(input.DoseVolume), Route = input.Route,
            InjectionSite = Validation.Clean(input.InjectionSite), BatchId = batch?.Id, BatchNumber = batch?.BatchNumber ?? Validation.Clean(input.BatchNumber),
            ExpiryDate = batch?.ExpiryDate ?? input.ExpiryDate, AdministeredBy = input.AdministeredBy.Trim(), Remarks = Validation.Clean(input.Remarks),
            EnteredByUserId = Session.User?.Id, IsHistoricalEntry = input.Historical
        };
        db.VaccinationRecords.Add(record);
        db.SaveChanges();
        record.RecordNo = $"VR-{record.Id:D6}";

        if (batch is not null)
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionNo = $"IT-{DateTime.Now:yyyyMMddHHmmssfff}-{record.Id}", BatchId = batch.Id, Type = InventoryTransactionType.Administered, Quantity = -1,
                Reason = $"{record.RecordNo} {patient.PatientNo}", VaccinationRecordId = record.Id, Username = Session.Username
            });

        appointment.Status = AppointmentStatus.Completed;
        appointment.AdministeredDate = input.Date;
        appointment.VaccinationRecordId = record.Id;
        Audit.Add(db, input.Historical ? "Historical dose" : "Administer", "VaccinationRecords", record.RecordNo,
            $"{patient.PatientNo} {vaccine.Name} {appointment.DoseLabel} batch {record.BatchNumber}");
        db.SaveChanges();

        ScheduleService.Recalculate(db, appointment.CourseId);
        db.SaveChanges();
        tx.Commit();

        var course2 = db.PatientCourses.Include(c => c.Appointments).AsNoTracking().First(c => c.Id == appointment.CourseId);
        var pending = course2.Appointments.Where(a => a.Status == AppointmentStatus.Pending).OrderBy(a => a.DueDate).ToList();
        var next = pending.FirstOrDefault();
        var msg = next is null
            ? $"{vaccine.Name} {appointment.DoseLabel} saved ({record.RecordNo}). The course is COMPLETE."
            : $"{vaccine.Name} {appointment.DoseLabel} saved ({record.RecordNo}). Next: {next.DoseLabel} on {Fmt.D(next.DueDate)}. Remaining doses: {pending.Count}.";
        if (course2.NeedsClinicalReview) msg += "\n\nCLINICAL REVIEW: " + course2.ReviewReason;
        return new AdministrationResult(record.Id, record.RecordNo, patient.Id, msg, next?.DueDate, pending.Count, course2.NeedsClinicalReview);
    }

    public static List<VaccinationRow> History(VaccinatorDbContext db, int? patientId = null, DateOnly? from = null, DateOnly? to = null, string? search = null, int? vaccineId = null)
    {
        var q = db.VaccinationRecords.AsNoTracking().Include(r => r.Patient).Include(r => r.Vaccine).AsQueryable();
        if (patientId is int pid) q = q.Where(r => r.PatientId == pid);
        if (from is DateOnly f) q = q.Where(r => r.AdministrationDate >= f);
        if (to is DateOnly t) q = q.Where(r => r.AdministrationDate <= t);
        if (vaccineId is int v) q = q.Where(r => r.VaccineId == v);
        var s = Validation.Clean(search)?.ToLower();
        if (s is not null) q = q.Where(r => r.Patient!.Name.ToLower().Contains(s) || r.Patient.PatientNo.ToLower().Contains(s) || r.Vaccine!.Name.ToLower().Contains(s)
                                            || (r.BatchNumber ?? "").ToLower().Contains(s));
        return q.OrderByDescending(r => r.AdministrationDate).ThenByDescending(r => r.Id).Take(5000).ToList()
            .Select(r => new VaccinationRow(r.Id, r.RecordNo, r.AdministrationDate, r.AdministrationTime, r.Patient!.PatientNo, r.Patient.Name, r.Vaccine!.Name,
                r.DoseLabel ?? $"Dose {r.DoseNumber}", r.Route.ToString(), r.InjectionSite, r.DoseVolume, r.BatchNumber, r.AdministeredBy, r.IsHistoricalEntry, r.Remarks))
            .ToList();
    }

    public static List<VaccinationRow> History(DateOnly? from = null, DateOnly? to = null, string? search = null, int? vaccineId = null)
    {
        using var db = DbFactory.Create();
        return History(db, null, from, to, search, vaccineId);
    }

    public static List<VaccineOption> Vaccines(bool includeInactive = false)
    {
        using var db = DbFactory.Create();
        return db.Vaccines.Where(v => includeInactive || v.IsActive).OrderBy(v => v.Name).Select(v => new VaccineOption(v.Id, v.Name)).ToList();
    }
}

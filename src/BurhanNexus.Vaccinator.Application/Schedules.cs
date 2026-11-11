using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

public sealed record ProtocolItem(int Id, int VaccineId, string VaccineName, string Name, string TargetGroup, ProtocolKind Kind, ScheduleAnchor Anchor,
    bool Approved, string? Reference, string? Warnings, string? CatchUpRule, int DoseCount)
{
    public string Display => $"{VaccineName} – {Name}";
    public string ApprovalText => Approved ? "Approved by center" : "Not yet approved by center";
    public bool IsRabies => Kind is ProtocolKind.RabiesPepIntramuscular or ProtocolKind.RabiesPepIntradermal or ProtocolKind.RabiesPepPreviouslyVaccinated
        or ProtocolKind.RabiesPreExposure or ProtocolKind.RabiesSpecialCategory;
}

public sealed record PreviewDose(int DoseNumber, string Label, string Schedule, DateOnly Date, string? Volume, string Route, string? Site)
{
    public string DateText => Fmt.D(Date);
    public string Weekday => Date.DayOfWeek.ToString();
}

public sealed record AppointmentRow(int AppointmentId, int PatientId, string PatientNo, string PatientName, string? Mobile, string Vaccine,
    int DoseNumber, string DoseLabel, DateOnly DueDate, DateOnly? PreviousDoseDate, string Status, int NotArrived, bool Rescheduled, bool NeedsReview)
{
    public string DueText => Fmt.D(DueDate);
    public string PreviousText => Fmt.D(PreviousDoseDate);
    public int DaysOverdue => Math.Max(0, Fmt.Today.DayNumber - DueDate.DayNumber);
    public string Note => (Rescheduled ? "Rescheduled. " : "") + (NotArrived > 0 ? $"Not arrived ×{NotArrived}. " : "") + (NeedsReview ? "Clinical review. " : "");
}

public enum AppointmentFilter { Today, Tomorrow, Next7Days, Next30Days, Overdue, AllUpcoming }

public static class ScheduleService
{
    public static string StatusOf(Appointment a)
    {
        if (a.Status == AppointmentStatus.Completed) return "Completed";
        if (a.Status == AppointmentStatus.Cancelled) return "Cancelled";
        if (a.DueDate < Fmt.Today) return "Overdue";
        return a.DueDate == Fmt.Today ? "Due today" : "Pending";
    }

    public static List<ProtocolItem> Protocols(int? vaccineId = null, bool includeInactive = false)
    {
        using var db = DbFactory.Create();
        var q = db.VaccineProtocols.Include(p => p.Vaccine).Include(p => p.Doses).AsQueryable();
        if (vaccineId is int v) q = q.Where(p => p.VaccineId == v);
        if (!includeInactive) q = q.Where(p => p.IsActive && p.Vaccine!.IsActive);
        return q.OrderBy(p => p.SortOrder).ToList().Select(ToItem).ToList();
    }

    public static ProtocolItem ToItem(VaccineProtocol p) => new(p.Id, p.VaccineId, p.Vaccine?.Name ?? "", p.Name, p.TargetGroup, p.Kind, p.Anchor,
        p.ApprovedByCenter, p.Reference, p.Warnings, p.CatchUpRule, p.Doses.Count);

    /// <summary>"Check Vaccine Schedule": the dates a protocol gives for an anchor date, without saving anything.</summary>
    public static List<PreviewDose> Preview(int protocolId, DateOnly anchor)
    {
        using var db = DbFactory.Create();
        var p = db.VaccineProtocols.Include(x => x.Doses).Include(x => x.Vaccine).First(x => x.Id == protocolId);
        var doses = p.Doses.OrderBy(d => d.DoseNumber).ToList();
        var calc = ScheduleCalculator.Calculate(doses.Select(ScheduleCalculator.ToDefinition), anchor);
        return calc.Select(c =>
        {
            var d = doses.First(x => x.DoseNumber == c.DoseNumber);
            var schedule = d.MinIntervalValue is int mv && d.OffsetValue == 0 && d.DoseNumber > 1
                ? $"{DateMath.Describe(mv, d.MinIntervalUnit!.Value)} after previous dose"
                : (p.Anchor == ScheduleAnchor.DateOfBirth ? (d.OffsetValue == 0 ? "At birth" : DateMath.Describe(d.OffsetValue, d.OffsetUnit) + " of age")
                    : (d.OffsetUnit == IntervalUnit.Days ? $"Day {d.OffsetValue}" : DateMath.Describe(d.OffsetValue, d.OffsetUnit) + " after first dose"));
            return new PreviewDose(c.DoseNumber, c.Label, schedule, c.CalculatedDate, d.DoseVolume ?? p.Vaccine!.DoseVolume,
                Fmt.RouteName(d.Route ?? p.Vaccine!.Route), d.InjectionSite ?? p.Vaccine!.InjectionSite);
        }).ToList();
    }

    /// <summary>Creates the course and saves every dose as its own appointment.</summary>
    public static PatientCourse StartCourse(VaccinatorDbContext db, int patientId, int protocolId, DateOnly anchor, int? rabiesCaseId = null)
    {
        var patient = db.Patients.Find(patientId) ?? throw new UserException("Patient not found.");
        var protocol = db.VaccineProtocols.Include(p => p.Doses).FirstOrDefault(p => p.Id == protocolId) ?? throw new UserException("Protocol not found.");
        if (!protocol.IsActive) throw new UserException("This protocol is switched off in Settings.");
        if (protocol.Doses.Count == 0) throw new UserException("This protocol has no doses defined.");

        if (protocol.Anchor == ScheduleAnchor.DateOfBirth)
            anchor = patient.DateOfBirth ?? throw new UserException("This protocol is calculated from the date of birth. Enter the patient's date of birth first.");

        if (db.PatientCourses.Any(c => c.PatientId == patientId && c.ProtocolId == protocolId && c.Status == CourseStatus.Active))
            throw new UserException("This patient already has an active course of this protocol. Record the next dose on the existing course.");

        var course = new PatientCourse { PatientId = patientId, ProtocolId = protocolId, AnchorDate = anchor, Status = CourseStatus.Active, AntiRabiesCaseId = rabiesCaseId };
        if (patient.IsImmunocompromised) { course.NeedsClinicalReview = true; course.ReviewReason = "Patient is marked immunocompromised – clinical review needed."; }

        var calc = ScheduleCalculator.Calculate(protocol.Doses.Select(ScheduleCalculator.ToDefinition), anchor);
        foreach (var c in calc)
        {
            var dose = protocol.Doses.First(d => d.DoseNumber == c.DoseNumber);
            course.Appointments.Add(new Appointment
            {
                PatientId = patientId, VaccineId = protocol.VaccineId, ProtocolDoseId = dose.Id, DoseNumber = c.DoseNumber, DoseLabel = c.Label,
                CalculatedDate = c.CalculatedDate, DueDate = c.CalculatedDate, Status = AppointmentStatus.Pending
            });
        }
        db.PatientCourses.Add(course);
        Audit.Add(db, "Start course", "PatientCourses", patient.PatientNo, $"{protocol.Name} from {Fmt.D(anchor)}");
        return course;
    }

    public static int StartCourse(int patientId, int protocolId, DateOnly anchor)
    {
        Session.Require(Module.Vaccination);
        using var db = DbFactory.Create();
        var course = StartCourse(db, patientId, protocolId, anchor);
        db.SaveChanges();
        return course.Id;
    }

    /// <summary>Creates every Pakistan EPI course for a newborn, calculated from the date of birth.</summary>
    public static void StartEpiSchedule(int patientId)
    {
        using var db = DbFactory.Create();
        var patient = db.Patients.Find(patientId)!;
        var epi = db.VaccineProtocols.Where(p => p.Kind == ProtocolKind.ChildhoodEpi && p.IsActive).OrderBy(p => p.SortOrder).ToList();
        foreach (var p in epi)
            if (!db.PatientCourses.Any(c => c.PatientId == patientId && c.ProtocolId == p.Id))
                StartCourse(db, patientId, p.Id, patient.DateOfBirth!.Value);
        db.SaveChanges();
    }

    /// <summary>
    /// Recalculates the open doses of a course after a dose was given or moved. Doses already given keep their dates.
    /// A late dose pushes later doses forward (minimum intervals kept) and may flag the course for clinical review.
    /// </summary>
    public static void Recalculate(VaccinatorDbContext db, int courseId)
    {
        var course = db.PatientCourses.Include(c => c.Appointments).Include(c => c.Protocol).ThenInclude(p => p!.Doses).First(c => c.Id == courseId);
        var protocol = course.Protocol!;
        var given = course.Appointments.Where(a => a.Status == AppointmentStatus.Completed && a.AdministeredDate != null)
            .ToDictionary(a => a.DoseNumber, a => a.AdministeredDate!.Value);
        var moved = course.Appointments.Where(a => a.Status == AppointmentStatus.Pending && a.IsRescheduled).ToDictionary(a => a.DoseNumber, a => a.DueDate);

        var calc = ScheduleCalculator.Calculate(protocol.Doses.Select(ScheduleCalculator.ToDefinition), course.AnchorDate, given, protocol.ReviewIfLateByDays, moved);
        foreach (var c in calc)
        {
            var a = course.Appointments.FirstOrDefault(x => x.DoseNumber == c.DoseNumber);
            if (a is null || a.Status != AppointmentStatus.Pending) continue;
            a.CalculatedDate = c.CalculatedDate;
            if (!a.IsRescheduled || a.DueDate < c.CalculatedDate) { a.DueDate = c.CalculatedDate; a.IsRescheduled = false; }
        }

        var late = calc.Where(c => c.LateEnoughForReview).ToList();
        if (late.Count > 0 && !course.NeedsClinicalReview)
        {
            course.NeedsClinicalReview = true;
            course.ReviewReason = $"{string.Join(", ", late.Select(l => l.Label))} given more than {protocol.ReviewIfLateByDays} days late. " +
                                  "The software continued the schedule without restarting it; a healthcare professional should confirm the catch-up plan.";
        }

        if (course.Appointments.All(a => a.Status != AppointmentStatus.Pending) && course.Status == CourseStatus.Active)
            course.Status = CourseStatus.Completed;
    }

    public static void Reschedule(int appointmentId, DateOnly newDate, string? reason)
    {
        Session.Require(Module.Appointments);
        using var db = DbFactory.Create();
        var a = db.Appointments.Include(x => x.Course).FirstOrDefault(x => x.Id == appointmentId) ?? throw new UserException("Appointment not found.");
        if (a.Status != AppointmentStatus.Pending) throw new UserException("Only pending doses can be rescheduled.");
        if (newDate < Fmt.Today) throw new UserException("The new date cannot be in the past.");
        if (newDate < a.CalculatedDate)
            throw new UserException($"The earliest allowed date for this dose is {Fmt.D(a.CalculatedDate)} (minimum interval of the protocol).");
        a.DueDate = newDate;
        a.IsRescheduled = true;
        a.Notes = Validation.Clean(reason) ?? a.Notes;
        Audit.Add(db, "Reschedule", "Appointments", a.Id, $"{a.DoseLabel} to {Fmt.D(newDate)}");
        db.SaveChanges();
        Recalculate(db, a.CourseId);
        db.SaveChanges();
    }

    public static void MarkNotArrived(int appointmentId)
    {
        Session.Require(Module.Appointments);
        using var db = DbFactory.Create();
        var a = db.Appointments.Find(appointmentId) ?? throw new UserException("Appointment not found.");
        if (a.Status != AppointmentStatus.Pending) throw new UserException("This dose is not pending.");
        a.NotArrivedCount++;
        a.LastNotArrivedOn = Fmt.Today;
        Audit.Add(db, "Not arrived", "Appointments", a.Id, a.DoseLabel);
        db.SaveChanges();
    }

    public static void StopCourse(int courseId, string reason)
    {
        Session.Require(Module.Vaccination);
        if (string.IsNullOrWhiteSpace(reason)) throw new UserException("Write the reason for stopping the course.");
        using var db = DbFactory.Create();
        var c = db.PatientCourses.Include(x => x.Appointments).First(x => x.Id == courseId);
        c.Status = CourseStatus.Stopped;
        foreach (var a in c.Appointments.Where(a => a.Status == AppointmentStatus.Pending)) { a.Status = AppointmentStatus.Cancelled; a.Notes = reason; }
        Audit.Add(db, "Stop course", "PatientCourses", courseId, reason);
        db.SaveChanges();
    }

    public static List<DoseRow> DosesForPatient(VaccinatorDbContext db, int patientId)
    {
        var list = db.Appointments.AsNoTracking().Include(a => a.Vaccine).Include(a => a.Course).ThenInclude(c => c!.Protocol)
            .Where(a => a.PatientId == patientId).ToList();
        var batches = db.VaccinationRecords.Where(r => r.PatientId == patientId).Select(r => new { r.Id, r.BatchNumber }).ToDictionary(r => r.Id, r => r.BatchNumber);
        return list.OrderBy(a => a.DueDate).ThenBy(a => a.Vaccine!.Name)
            .Select(a => new DoseRow(a.Id, a.CourseId, a.Vaccine!.Name, a.Course!.Protocol!.Name, a.DoseNumber, a.DoseLabel, a.DueDate, a.CalculatedDate,
                StatusOf(a), a.AdministeredDate, a.VaccinationRecordId is int r && batches.TryGetValue(r, out var b) ? b : null,
                a.Course.NeedsClinicalReview, a.NotArrivedCount))
            .ToList();
    }

    public static List<DoseRow> DosesForPatient(int patientId)
    {
        using var db = DbFactory.Create();
        return DosesForPatient(db, patientId);
    }

    public static List<AppointmentRow> Appointments(AppointmentFilter filter, string? search = null)
    {
        using var db = DbFactory.Create();
        var today = Fmt.Today;
        var tomorrow = today.AddDays(1);
        var in7 = today.AddDays(7);
        var in30 = today.AddDays(30);
        var q = db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Vaccine).Include(a => a.Course)
            .Where(a => a.Status == AppointmentStatus.Pending && a.Course!.Status == CourseStatus.Active);
        q = filter switch
        {
            AppointmentFilter.Today => q.Where(a => a.DueDate == today),
            AppointmentFilter.Tomorrow => q.Where(a => a.DueDate == tomorrow),
            AppointmentFilter.Next7Days => q.Where(a => a.DueDate >= today && a.DueDate <= in7),
            AppointmentFilter.Next30Days => q.Where(a => a.DueDate >= today && a.DueDate <= in30),
            AppointmentFilter.Overdue => q.Where(a => a.DueDate < today),
            _ => q.Where(a => a.DueDate >= today)
        };
        var rows = q.OrderBy(a => a.DueDate).Take(3000).ToList();
        var t = Validation.Clean(search)?.ToLower();
        if (t is not null)
            rows = rows.Where(a => a.Patient!.Name.ToLower().Contains(t) || a.Patient.PatientNo.ToLower().Contains(t) || (a.Patient.Mobile ?? "").Contains(t)
                                   || a.Vaccine!.Name.ToLower().Contains(t)).ToList();

        var courseIds = rows.Select(r => r.CourseId).Distinct().ToList();
        var previous = db.Appointments.Where(a => courseIds.Contains(a.CourseId) && a.Status == AppointmentStatus.Completed)
            .Select(a => new { a.CourseId, a.DoseNumber, a.AdministeredDate }).ToList();

        return rows.Select(a => new AppointmentRow(a.Id, a.PatientId, a.Patient!.PatientNo, a.Patient.Name, a.Patient.Mobile, a.Vaccine!.Name, a.DoseNumber,
            a.DoseLabel, a.DueDate,
            previous.Where(p => p.CourseId == a.CourseId && p.DoseNumber < a.DoseNumber).OrderByDescending(p => p.DoseNumber).Select(p => p.AdministeredDate).FirstOrDefault(),
            StatusOf(a), a.NotArrivedCount, a.IsRescheduled, a.Course!.NeedsClinicalReview)).ToList();
    }

    public static List<ProtocolDose> DosesOf(int protocolId)
    {
        using var db = DbFactory.Create();
        return db.VaccineDoses.Where(d => d.ProtocolId == protocolId).OrderBy(d => d.DoseNumber).ToList();
    }
}

namespace BurhanNexus.Vaccinator.Domain;

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class User : Entity
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Simple key/value settings (center name, address, formats, developer contact...).</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}

public class Doctor : Entity
{
    public string Name { get; set; } = "";
    /// <summary>Lower-case name without "Dr." and extra spaces. Unique: prevents duplicates like "Dr. Ali" and "ali".</summary>
    public string NormalizedName { get; set; } = "";
    public string? Specialization { get; set; }
    public string? Hospital { get; set; }
    public string? ContactNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var text = name.Trim().ToLowerInvariant().Replace('.', ' ');
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1 && (parts[0] == "dr" || parts[0] == "doctor")) parts.RemoveAt(0);
        return string.Join(' ', parts);
    }
}

public class Patient : Entity
{
    public string PatientNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string? GuardianName { get; set; }
    public int? AgeYears { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? Address { get; set; }
    public string? Cnic { get; set; }
    public string? Mobile { get; set; }
    public string? AlternativeMobile { get; set; }
    public int? AdvisedByDoctorId { get; set; }
    public Doctor? AdvisedByDoctor { get; set; }
    public string? AdvisedByName { get; set; }
    public DateOnly RegistrationDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public PatientCategory Category { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Flag for professional review (immunocompromised etc.). Set by staff, never inferred.</summary>
    public bool IsImmunocompromised { get; set; }
    public bool IsArchived { get; set; }

    public NewbornDetail? Newborn { get; set; }
    public List<PatientCourse> Courses { get; set; } = new();
    public List<VaccinationRecord> Vaccinations { get; set; } = new();
}

/// <summary>Extra newborn data (table NewbornPatients). Shares the patient's ID.</summary>
public class NewbornDetail
{
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? MotherMobile { get; set; }
    public TimeOnly? TimeOfBirth { get; set; }
    public string? BirthPlace { get; set; }
    public decimal? BirthWeightKg { get; set; }
}

public class Vaccine : Entity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string DiseasePrevented { get; set; } = "";
    public string? RecommendedAge { get; set; }
    public string? DoseVolume { get; set; }
    public AdministrationRoute Route { get; set; }
    public string? InjectionSite { get; set; }
    public string? MinimumAge { get; set; }
    public string? BoosterRequirements { get; set; }
    public string? StorageTemperature { get; set; }
    public string? SpecialInstructions { get; set; }
    public string? Reference { get; set; }
    public DateOnly LastUpdated { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool IsActive { get; set; } = true;
    public List<VaccineProtocol> Protocols { get; set; } = new();
}

public class VaccineProtocol : Entity
{
    public int VaccineId { get; set; }
    public Vaccine? Vaccine { get; set; }
    public string Name { get; set; } = "";
    public string TargetGroup { get; set; } = "";
    public ProtocolKind Kind { get; set; }
    public ScheduleAnchor Anchor { get; set; }
    public string? Description { get; set; }
    public string? Warnings { get; set; }
    public string? CatchUpRule { get; set; }
    /// <summary>If any dose is given this many days after its due date, the course is flagged for professional review.</summary>
    public int? ReviewIfLateByDays { get; set; }
    public string? Reference { get; set; }
    /// <summary>The center's medical officer has reviewed and approved this protocol for use.</summary>
    public bool ApprovedByCenter { get; set; }
    public string? ApprovedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<ProtocolDose> Doses { get; set; } = new();
}

/// <summary>One dose of a protocol (table VaccineDoses).</summary>
public class ProtocolDose
{
    public int Id { get; set; }
    public int ProtocolId { get; set; }
    public VaccineProtocol? Protocol { get; set; }
    public int DoseNumber { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Time from the anchor (DOB or first dose) to this dose.</summary>
    public int OffsetValue { get; set; }
    public IntervalUnit OffsetUnit { get; set; }
    /// <summary>Minimum time after the previous dose. Null = the difference of the offsets.</summary>
    public int? MinIntervalValue { get; set; }
    public IntervalUnit? MinIntervalUnit { get; set; }
    public string? DoseVolume { get; set; }
    public AdministrationRoute? Route { get; set; }
    public string? InjectionSite { get; set; }
    public bool IsBooster { get; set; }
}

/// <summary>A protocol started for one patient. Each dose is an Appointment row.</summary>
public class PatientCourse : Entity
{
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public int ProtocolId { get; set; }
    public VaccineProtocol? Protocol { get; set; }
    public DateOnly AnchorDate { get; set; }
    public CourseStatus Status { get; set; }
    public bool NeedsClinicalReview { get; set; }
    public string? ReviewReason { get; set; }
    public int? AntiRabiesCaseId { get; set; }
    public List<Appointment> Appointments { get; set; } = new();
}

/// <summary>A scheduled dose (table Appointments). Saved separately for every dose.</summary>
public class Appointment : Entity
{
    public int CourseId { get; set; }
    public PatientCourse? Course { get; set; }
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public int VaccineId { get; set; }
    public Vaccine? Vaccine { get; set; }
    public int ProtocolDoseId { get; set; }
    public ProtocolDose? ProtocolDose { get; set; }
    public int DoseNumber { get; set; }
    public string DoseLabel { get; set; } = "";
    /// <summary>Date calculated from the protocol.</summary>
    public DateOnly CalculatedDate { get; set; }
    /// <summary>The date the patient is expected (calculated or rescheduled).</summary>
    public DateOnly DueDate { get; set; }
    public bool IsRescheduled { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateOnly? AdministeredDate { get; set; }
    public int? VaccinationRecordId { get; set; }
    public int NotArrivedCount { get; set; }
    public DateOnly? LastNotArrivedOn { get; set; }
    public string? Notes { get; set; }
}

public class VaccinationRecord : Entity
{
    public string RecordNo { get; set; } = "";
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public int VaccineId { get; set; }
    public Vaccine? Vaccine { get; set; }
    public int? AppointmentId { get; set; }
    public int? CourseId { get; set; }
    public int? AntiRabiesCaseId { get; set; }
    public int DoseNumber { get; set; }
    public string? DoseLabel { get; set; }
    public DateOnly AdministrationDate { get; set; }
    public TimeOnly AdministrationTime { get; set; }
    public string? DoseVolume { get; set; }
    public AdministrationRoute Route { get; set; }
    public string? InjectionSite { get; set; }
    public int? BatchId { get; set; }
    public VaccineBatch? Batch { get; set; }
    public string? BatchNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string AdministeredBy { get; set; } = "";
    public string? Remarks { get; set; }
    public int? EnteredByUserId { get; set; }
    /// <summary>True when a past dose is recorded from a card (no stock deduction).</summary>
    public bool IsHistoricalEntry { get; set; }
}

public class AntiRabiesCase : Entity
{
    public string CaseNo { get; set; } = "";
    public int PatientId { get; set; }
    public Patient? Patient { get; set; }
    public DateOnly BiteDate { get; set; }
    public DateOnly FirstVaccineDate { get; set; }
    public AnimalType AnimalType { get; set; }
    public string? AnimalOther { get; set; }
    public BiteCategory Category { get; set; }
    public string? BiteLocation { get; set; }
    public bool PreviouslyVaccinated { get; set; }
    public string? PreviousVaccinationDetails { get; set; }
    public string? VaccineBrand { get; set; }
    public AdministrationRoute Route { get; set; }
    public int ProtocolId { get; set; }
    public VaccineProtocol? Protocol { get; set; }
    public bool WoundWashed { get; set; }
    public RigType Rig { get; set; }
    public string? RigDose { get; set; }
    public string? RigBatch { get; set; }
    public DateOnly? RigDate { get; set; }
    public bool Immunocompromised { get; set; }
    public string? AdvisedBy { get; set; }
    public string? Remarks { get; set; }
    public int? CourseId { get; set; }
}

public class VaccineBatch : Entity
{
    public int VaccineId { get; set; }
    public Vaccine? Vaccine { get; set; }
    public string? Brand { get; set; }
    public string? Manufacturer { get; set; }
    public string BatchNumber { get; set; } = "";
    public int QuantityReceived { get; set; }
    public int QuantityAvailable { get; set; }
    public string? VialSize { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public string? Supplier { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public string? StorageTemperature { get; set; }
    public VvmStatus Vvm { get; set; }
    public string? StockLocation { get; set; }
    public bool IsQuarantined { get; set; }
    public string? QuarantineReason { get; set; }
    public int LowStockLevel { get; set; } = 10;

    public bool IsExpired(DateOnly today) => ExpiryDate < today;

    /// <summary>Expired, quarantined, VVM stage 3/4 or empty stock may never be issued.</summary>
    public string? WhyNotUsable(DateOnly today)
    {
        if (IsExpired(today)) return "Batch has expired";
        if (IsQuarantined) return "Batch is quarantined";
        if (Vvm is VvmStatus.Stage3 or VvmStatus.Stage4) return "VVM has reached the discard point (stage 3/4)";
        if (QuantityAvailable <= 0) return "No stock left in this batch";
        return null;
    }
}

public class InventoryTransaction
{
    public long Id { get; set; }
    public string TransactionNo { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    public int BatchId { get; set; }
    public VaccineBatch? Batch { get; set; }
    public InventoryTransactionType Type { get; set; }
    /// <summary>Positive adds stock, negative removes stock.</summary>
    public int Quantity { get; set; }
    public string? Reason { get; set; }
    public int? VaccinationRecordId { get; set; }
    public string Username { get; set; } = "";
}

public class ColdChainLog : Entity
{
    public DateTime RecordedAt { get; set; } = DateTime.Now;
    public string Location { get; set; } = "";
    public decimal TemperatureC { get; set; }
    public string RecordedBy { get; set; } = "";
    public string? Remarks { get; set; }
    public bool IsExcursion => TemperatureC < 2m || TemperatureC > 8m;
}

public class InsulinProduct : Entity
{
    public string Brand { get; set; } = "";
    public string InsulinType { get; set; } = "";
    public string Strength { get; set; } = "";
    public string VialSize { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int LowStockLevel { get; set; } = 10;
    public string? StorageInstructions { get; set; }
    public bool IsActive { get; set; } = true;
}

public class InsulinBatch : Entity
{
    public int ProductId { get; set; }
    public InsulinProduct? Product { get; set; }
    public string BatchNumber { get; set; } = "";
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityAvailable { get; set; }
    public string? StorageLocation { get; set; }
    public decimal? StorageTemperatureC { get; set; }
    public DateOnly ReceivedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public class InsulinTransaction
{
    public long Id { get; set; }
    public string TransactionNo { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    public InsulinTransactionType Type { get; set; }
    public int BatchId { get; set; }
    public InsulinBatch? Batch { get; set; }
    public int? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public DateOnly IssueDate { get; set; }
    /// <summary>Vials. Positive for stock in, negative for issue/wastage.</summary>
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string? PrescribingDoctor { get; set; }
    /// <summary>Exactly as written by the prescriber. The software never calculates insulin doses.</summary>
    public string? PrescribedDosage { get; set; }
    public string? Remarks { get; set; }
    public string Username { get; set; } = "";
}

/// <summary>Every printed document is logged so the patient profile can show it.</summary>
public class PrintLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public int? PatientId { get; set; }
    public string Document { get; set; } = "";
    public string Username { get; set; } = "";
}

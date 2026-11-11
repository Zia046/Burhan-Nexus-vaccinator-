namespace BurhanNexus.Vaccinator.Domain;

public enum Gender { Male, Female }

public enum PatientCategory
{
    General,
    Child,
    Newborn,
    Adult,
    PregnantWoman,
    AntiRabies,
    Insulin,
    Traveller,
    HealthcareWorker,
    Other
}

public enum UserRole { Administrator, Vaccinator, Receptionist, InventoryManager }

/// <summary>Unit of a dose offset or interval. Months and years are real calendar months/years, never fixed 30/365 days.</summary>
public enum IntervalUnit { Days, Weeks, Months, Years }

/// <summary>Where a schedule starts: the patient's date of birth (EPI) or the date the first dose is given.</summary>
public enum ScheduleAnchor { FirstDose, DateOfBirth }

public enum ProtocolKind
{
    Routine,
    ChildhoodEpi,
    RabiesPepIntramuscular,
    RabiesPepIntradermal,
    RabiesPepPreviouslyVaccinated,
    RabiesPreExposure,
    RabiesSpecialCategory
}

public enum AppointmentStatus { Pending, Completed, Cancelled }

public enum CourseStatus { Active, Completed, Stopped }

public enum AnimalType { Dog, Cat, Monkey, Other }

public enum BiteCategory { I = 1, II = 2, III = 3 }

public enum AdministrationRoute { IM, ID, SC, Oral, Other }

public enum RigType { None, HRIG, ERIG, MonoclonalAntibody }

public enum VvmStatus { NotApplicable, Stage1, Stage2, Stage3, Stage4 }

public enum InventoryTransactionType { StockIn, StockOut, Administered, Wastage, Adjustment, Expired }

public enum InsulinTransactionType { StockIn, Issue, Wastage, Adjustment }

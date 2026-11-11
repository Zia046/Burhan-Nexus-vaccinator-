using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

public sealed class PatientInput
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public string? GuardianName { get; set; }
    public int? AgeYears { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? Address { get; set; }
    public string? Cnic { get; set; }
    public string? Mobile { get; set; }
    public string? AlternativeMobile { get; set; }
    public string? AdvisedBy { get; set; }
    public DateOnly RegistrationDate { get; set; } = Fmt.Today;
    public PatientCategory Category { get; set; }
    public string? Remarks { get; set; }
    public bool IsImmunocompromised { get; set; }
}

public sealed class NewbornInput
{
    public string? BabyName { get; set; }
    public Gender Gender { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public TimeOnly? TimeOfBirth { get; set; }
    public string FatherName { get; set; } = "";
    public string? MotherName { get; set; }
    public string? MotherMobile { get; set; }
    public string? Address { get; set; }
    public string? BirthPlace { get; set; }
    public decimal? BirthWeightKg { get; set; }
    public DateOnly RegistrationDate { get; set; } = Fmt.Today;
    public string? AdvisedBy { get; set; }
}

/// <summary>Thrown when a similar patient exists. The UI asks the user and calls again with allowSimilar = true.</summary>
public sealed class PossibleDuplicateException : Exception
{
    public PossibleDuplicateException(string message, IReadOnlyList<PatientRow> matches) : base(message) => Matches = matches;
    public IReadOnlyList<PatientRow> Matches { get; }
}

public sealed record PatientRow(int Id, string PatientNo, string Name, string? GuardianName, string? Cnic, string? Mobile,
    Gender Gender, string AgeText, string Category, string? Doctor, DateOnly RegistrationDate)
{
    public string RegisteredText => Fmt.D(RegistrationDate);
    public string Display => $"{PatientNo} – {Name}" + (GuardianName is null ? "" : $" ({GuardianName})");
}

public sealed record DoseRow(int AppointmentId, int CourseId, string Vaccine, string Protocol, int DoseNumber, string DoseLabel,
    DateOnly DueDate, DateOnly CalculatedDate, string Status, DateOnly? GivenOn, string? BatchNumber, bool NeedsReview, int NotArrived)
{
    public string DueText => Fmt.D(DueDate);
    public string GivenText => Fmt.D(GivenOn);
    public bool IsOverdue => Status == "Overdue";
}

public sealed record VaccinationRow(int Id, string RecordNo, DateOnly Date, TimeOnly Time, string PatientNo, string PatientName, string Vaccine,
    string Dose, string Route, string? Site, string? Volume, string? Batch, string AdministeredBy, bool Historical, string? Remarks)
{
    public string DateText => Fmt.D(Date);
    public string TimeText => Fmt.T(Time);
}

public sealed record InsulinRow(long Id, string TransactionNo, DateOnly Date, string? PatientNo, string? PatientName, string Product, string Batch,
    int Vials, decimal UnitPrice, decimal Total, string? Doctor, string? Dosage, string Type)
{
    public string DateText => Fmt.D(Date);
    public string TotalText => Fmt.Money(Total);
}

public sealed record RabiesCaseRow(int Id, string CaseNo, int PatientId, string PatientNo, string PatientName, string? Mobile, DateOnly BiteDate,
    string Animal, string Category, string Protocol, int DosesGiven, int TotalDoses, DateOnly? NextDue, string Status, bool Alert)
{
    public string BiteText => Fmt.D(BiteDate);
    public string NextText => Fmt.D(NextDue);
    public string Progress => $"{DosesGiven} / {TotalDoses}";
}

public sealed record PrintRow(DateTime At, string Document, string Username)
{
    public string AtText => Fmt.DT(At);
}

public sealed class PatientProfile
{
    public Patient Patient { get; init; } = new();
    public string AgeText { get; init; } = "";
    public string? DoctorName { get; init; }
    public List<VaccinationRow> History { get; init; } = new();
    public List<DoseRow> Doses { get; init; } = new();
    public List<DoseRow> Upcoming => Doses.Where(d => d.Status is "Pending" or "Due today").OrderBy(d => d.DueDate).ToList();
    public List<DoseRow> Missed => Doses.Where(d => d.Status == "Overdue").OrderBy(d => d.DueDate).ToList();
    public List<RabiesCaseRow> RabiesCases { get; init; } = new();
    public List<InsulinRow> Insulin { get; init; } = new();
    public List<PrintRow> Prints { get; init; } = new();
    public List<DateOnly> VisitDates => History.Select(h => h.Date).Concat(Insulin.Select(i => i.Date)).Distinct().OrderByDescending(d => d).ToList();
    public DoseRow? NextAppointment => Doses.Where(d => d.Status != "Completed").OrderBy(d => d.DueDate).FirstOrDefault();
}

public static class PatientService
{
    public static string AgeText(DateOnly? dob, int? ageYears)
    {
        if (dob is DateOnly d) return DateMath.Age(d, Fmt.Today).Text;
        return ageYears is int y ? $"{y} year(s)" : "";
    }

    public static int Register(PatientInput input, bool allowSimilar = false)
    {
        Session.Require(Module.Patients);
        using var db = DbFactory.Create();
        var name = Validation.Clean(input.Name) ?? throw new UserException("Enter the patient name.");
        var cnic = Validation.Cnic(input.Cnic);
        var mobile = Validation.Mobile(input.Mobile);
        var alt = Validation.Mobile(input.AlternativeMobile, "Alternative contact number");
        CheckAge(input);

        if (cnic is not null)
        {
            var same = db.Patients.FirstOrDefault(p => p.Cnic == cnic && p.Id != (input.Id ?? 0));
            if (same is not null) throw new UserException($"A patient with CNIC {cnic} is already registered: {same.PatientNo} – {same.Name}. Open the existing patient instead.");
        }

        if (!allowSimilar && input.Id is null)
        {
            var guardian = Validation.Clean(input.GuardianName);
            var similar = db.Patients.Where(p => p.Name == name && ((guardian != null && p.GuardianName == guardian) || (mobile != null && p.Mobile == mobile)))
                .Take(5).ToList();
            if (similar.Count > 0)
                throw new PossibleDuplicateException("A patient with the same name and father/husband name or mobile number already exists. Is this the same person?",
                    similar.Select(ToRow).ToList());
        }

        Patient p;
        if (input.Id is int id)
        {
            p = db.Patients.Find(id) ?? throw new UserException("Patient not found.");
            Audit.Add(db, "Update", "Patients", p.PatientNo);
        }
        else
        {
            p = new Patient { PatientNo = "TMP-" + Guid.NewGuid().ToString("N") };
            db.Patients.Add(p);
        }

        p.Name = name;
        p.GuardianName = Validation.Clean(input.GuardianName);
        p.AgeYears = input.AgeYears;
        p.DateOfBirth = input.DateOfBirth;
        if (p.DateOfBirth is DateOnly dob && p.AgeYears is null) p.AgeYears = DateMath.Age(dob, Fmt.Today).Months / 12;
        p.Gender = input.Gender;
        p.Address = Validation.Clean(input.Address);
        p.Cnic = cnic;
        p.Mobile = mobile;
        p.AlternativeMobile = alt;
        var doctor = DoctorService.GetOrCreate(db, input.AdvisedBy);
        p.AdvisedByDoctor = doctor;
        p.AdvisedByName = doctor?.Name;
        p.RegistrationDate = input.RegistrationDate;
        p.Category = input.Category;
        p.Remarks = Validation.Clean(input.Remarks);
        p.IsImmunocompromised = input.IsImmunocompromised;

        using var tx = db.Database.BeginTransaction();
        db.SaveChanges();
        if (input.Id is null)
        {
            p.PatientNo = $"BNV-{p.Id:D6}";
            Audit.Add(db, "Register", "Patients", p.PatientNo, p.Name);
            db.SaveChanges();
        }
        tx.Commit();
        return p.Id;
    }

    private static void CheckAge(PatientInput i)
    {
        if (i.DateOfBirth is DateOnly d && d > Fmt.Today) throw new UserException("Date of birth cannot be in the future.");
        if (i.AgeYears is < 0 or > 130) throw new UserException("Enter a correct age.");
        if (i.RegistrationDate > Fmt.Today) throw new UserException("Registration date cannot be in the future.");
    }

    /// <summary>Registers a newborn and creates the full Pakistan EPI schedule from the date of birth.</summary>
    public static int RegisterNewborn(NewbornInput input)
    {
        Session.Require(Module.Newborn);
        if (input.DateOfBirth > Fmt.Today) throw new UserException("Date of birth cannot be in the future.");
        if (input.DateOfBirth < Fmt.Today.AddYears(-5)) throw new UserException("Newborn registration is for children under 5 years. Use New Patient Entry for older patients.");
        var father = Validation.Clean(input.FatherName) ?? throw new UserException("Enter the father's name.");
        var motherMobile = Validation.Mobile(input.MotherMobile, "Mother mobile number");
        if (input.BirthWeightKg is < 0.3m or > 8m) throw new UserException("Birth weight looks wrong. Enter it in kilograms (for example 3.2).");

        var name = Validation.Clean(input.BabyName) ?? $"Baby of {Validation.Clean(input.MotherName) ?? father}";
        int id;
        using (var db = DbFactory.Create())
        {
            var dup = db.Patients.Include(p => p.Newborn)
                .FirstOrDefault(p => p.Category == PatientCategory.Newborn && p.DateOfBirth == input.DateOfBirth && p.GuardianName == father && p.Gender == input.Gender
                    && (motherMobile == null || p.Mobile == motherMobile));
            if (dup is not null) throw new UserException($"This baby seems to be registered already: {dup.PatientNo} – {dup.Name}. Search the old patient instead.");

            var doctor = DoctorService.GetOrCreate(db, input.AdvisedBy);
            var p = new Patient
            {
                PatientNo = "TMP-" + Guid.NewGuid().ToString("N"), Name = name, GuardianName = father, DateOfBirth = input.DateOfBirth, AgeYears = 0,
                Gender = input.Gender, Address = Validation.Clean(input.Address), Mobile = motherMobile, Category = PatientCategory.Newborn,
                RegistrationDate = input.RegistrationDate, AdvisedByDoctor = doctor, AdvisedByName = doctor?.Name,
                Newborn = new NewbornDetail
                {
                    FatherName = father, MotherName = Validation.Clean(input.MotherName), MotherMobile = motherMobile, TimeOfBirth = input.TimeOfBirth,
                    BirthPlace = Validation.Clean(input.BirthPlace), BirthWeightKg = input.BirthWeightKg
                }
            };
            db.Patients.Add(p);
            using var tx = db.Database.BeginTransaction();
            db.SaveChanges();
            p.PatientNo = $"BNV-{p.Id:D6}";
            Audit.Add(db, "Register", "Newborn", p.PatientNo, p.Name);
            db.SaveChanges();
            tx.Commit();
            id = p.Id;
        }

        ScheduleService.StartEpiSchedule(id);
        return id;
    }

    public static PatientRow ToRow(Patient p) => new(p.Id, p.PatientNo, p.Name, p.GuardianName, p.Cnic, p.Mobile, p.Gender,
        AgeText(p.DateOfBirth, p.AgeYears), Fmt.Category(p.Category), p.AdvisedByName, p.RegistrationDate);

    public enum SearchField { Any, Name, PatientId, Cnic, Mobile, Guardian, Doctor }

    public static List<PatientRow> Search(string? text, SearchField field = SearchField.Any, int max = 300)
    {
        using var db = DbFactory.Create();
        var q = db.Patients.Where(p => !p.IsArchived);
        var t = Validation.Clean(text);
        if (t is not null)
        {
            var digits = Validation.Digits(t);
            var lower = t.ToLower();
            q = field switch
            {
                SearchField.Name => q.Where(p => p.Name.ToLower().Contains(lower)),
                SearchField.PatientId => q.Where(p => p.PatientNo.ToLower().Contains(lower)),
                SearchField.Cnic => q.Where(p => p.Cnic != null && p.Cnic.Replace("-", "").Contains(digits)),
                SearchField.Mobile => q.Where(p => (p.Mobile != null && p.Mobile.Contains(digits)) || (p.AlternativeMobile != null && p.AlternativeMobile.Contains(digits))),
                SearchField.Guardian => q.Where(p => p.GuardianName != null && p.GuardianName.ToLower().Contains(lower)),
                SearchField.Doctor => q.Where(p => p.AdvisedByName != null && p.AdvisedByName.ToLower().Contains(lower)),
                _ => digits.Length >= 4 && digits.Length == t.Replace("-", "").Replace(" ", "").Length
                    ? q.Where(p => p.PatientNo.Contains(digits) || (p.Cnic != null && p.Cnic.Replace("-", "").Contains(digits))
                                   || (p.Mobile != null && p.Mobile.Contains(digits)) || (p.AlternativeMobile != null && p.AlternativeMobile.Contains(digits)))
                    : q.Where(p => p.Name.ToLower().Contains(lower) || p.PatientNo.ToLower().Contains(lower)
                                   || (p.GuardianName != null && p.GuardianName.ToLower().Contains(lower))
                                   || (p.AdvisedByName != null && p.AdvisedByName.ToLower().Contains(lower)))
            };
        }
        return q.OrderByDescending(p => p.Id).Take(max).ToList().Select(ToRow).ToList();
    }

    public static PatientRow? Row(int id)
    {
        using var db = DbFactory.Create();
        var p = db.Patients.Find(id);
        return p is null ? null : ToRow(p);
    }

    public static PatientInput LoadInput(int id)
    {
        using var db = DbFactory.Create();
        var p = db.Patients.Find(id) ?? throw new UserException("Patient not found.");
        return new PatientInput
        {
            Id = p.Id, Name = p.Name, GuardianName = p.GuardianName, AgeYears = p.AgeYears, DateOfBirth = p.DateOfBirth, Gender = p.Gender,
            Address = p.Address, Cnic = p.Cnic, Mobile = p.Mobile, AlternativeMobile = p.AlternativeMobile, AdvisedBy = p.AdvisedByName,
            RegistrationDate = p.RegistrationDate, Category = p.Category, Remarks = p.Remarks, IsImmunocompromised = p.IsImmunocompromised
        };
    }

    public static PatientProfile Profile(int id)
    {
        using var db = DbFactory.Create();
        var p = db.Patients.Include(x => x.Newborn).AsNoTracking().FirstOrDefault(x => x.Id == id) ?? throw new UserException("Patient not found.");
        return new PatientProfile
        {
            Patient = p,
            AgeText = AgeText(p.DateOfBirth, p.AgeYears),
            DoctorName = p.AdvisedByName,
            History = VaccinationService.History(db, patientId: id),
            Doses = ScheduleService.DosesForPatient(db, id),
            RabiesCases = RabiesService.Cases(db, patientId: id),
            Insulin = InsulinService.Transactions(db, patientId: id),
            Prints = db.PrintLogs.Where(x => x.PatientId == id).OrderByDescending(x => x.At).Take(200).ToList()
                .Select(x => new PrintRow(x.At, x.Document, x.Username)).ToList()
        };
    }

    public static void LogPrint(int? patientId, string document)
    {
        using var db = DbFactory.Create();
        db.PrintLogs.Add(new PrintLog { PatientId = patientId, Document = document, Username = Session.Username });
        db.SaveChanges();
    }
}

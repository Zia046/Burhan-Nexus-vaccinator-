using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

public sealed class CenterProfile
{
    public string CenterName { get; set; } = "Vaccination Center";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public byte[]? Logo { get; set; }
    public string DateFormat { get; set; } = "dd-MM-yyyy";
    public bool Use24Hour { get; set; }
    public bool PrintDirectly { get; set; }
    public string DeveloperName { get; set; } = Branding.Developer;
    public string DeveloperEmail { get; set; } = Branding.DeveloperEmail;
    public string DeveloperMobile { get; set; } = Branding.DeveloperMobile;
}

public static class Branding
{
    public const string Product = "Burhan Nexus Vaccinator";
    public const string Developer = "Burhan Nexus";
    public const string DeveloperEmail = "ziaullah.046@gmail.com";
    public const string DeveloperMobile = "03475093446";
    public const string ReleaseDate = "November 2026";
    public static string Version => typeof(Branding).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public const string AboutText = "A modern healthcare management solution designed to simplify patient registration, vaccination scheduling, immunization tracking, Anti-Rabies management, insulin dispensing, and medical record management.";
}

public static class SettingsService
{
    private static readonly string[] Keys =
        { "CenterName", "Address", "Phone", "Email", "Logo", "DateFormat", "Use24Hour", "PrintDirectly", "DeveloperName", "DeveloperEmail", "DeveloperMobile" };

    public static CenterProfile Load()
    {
        using var db = DbFactory.Create();
        var all = db.Settings.ToDictionary(s => s.Key, s => s.Value);
        string? Get(string k) => all.TryGetValue(k, out var v) ? v : null;
        var p = new CenterProfile
        {
            CenterName = Get("CenterName") ?? "Vaccination Center",
            Address = Get("Address"), Phone = Get("Phone"), Email = Get("Email"),
            Logo = string.IsNullOrEmpty(Get("Logo")) ? null : Convert.FromBase64String(Get("Logo")!),
            DateFormat = Get("DateFormat") ?? "dd-MM-yyyy",
            Use24Hour = Get("Use24Hour") == "1",
            PrintDirectly = Get("PrintDirectly") == "1",
            DeveloperName = Get("DeveloperName") ?? Branding.Developer,
            DeveloperEmail = Get("DeveloperEmail") ?? Branding.DeveloperEmail,
            DeveloperMobile = Get("DeveloperMobile") ?? Branding.DeveloperMobile
        };
        Fmt.DateFormat = p.DateFormat;
        Fmt.Use24Hour = p.Use24Hour;
        return p;
    }

    public static void Save(CenterProfile p)
    {
        Session.Require(Module.Settings);
        if (string.IsNullOrWhiteSpace(p.CenterName)) throw new UserException("Enter the hospital / vaccination center name.");
        if (p.DateFormat is not ("dd-MM-yyyy" or "dd/MM/yyyy" or "yyyy-MM-dd" or "dd-MMM-yyyy")) p.DateFormat = "dd-MM-yyyy";
        using var db = DbFactory.Create();
        var values = new Dictionary<string, string?>
        {
            ["CenterName"] = p.CenterName.Trim(), ["Address"] = Validation.Clean(p.Address), ["Phone"] = Validation.Clean(p.Phone),
            ["Email"] = Validation.Clean(p.Email), ["Logo"] = p.Logo is null ? null : Convert.ToBase64String(p.Logo),
            ["DateFormat"] = p.DateFormat, ["Use24Hour"] = p.Use24Hour ? "1" : "0", ["PrintDirectly"] = p.PrintDirectly ? "1" : "0",
            ["DeveloperName"] = Validation.Clean(p.DeveloperName) ?? Branding.Developer,
            ["DeveloperEmail"] = Validation.Clean(p.DeveloperEmail), ["DeveloperMobile"] = Validation.Clean(p.DeveloperMobile)
        };
        foreach (var (k, v) in values)
        {
            var row = db.Settings.Find(k);
            if (row is null) db.Settings.Add(new Setting { Key = k, Value = v }); else row.Value = v;
        }
        Audit.Add(db, "Update", "Settings", null, "Center profile / formats changed");
        db.SaveChanges();
        Fmt.DateFormat = p.DateFormat;
        Fmt.Use24Hour = p.Use24Hour;
    }
}

public sealed record UserRow(int Id, string Username, string FullName, UserRole Role, bool IsActive)
{
    public string RoleText => Permissions.RoleName(Role);
    public string StatusText => IsActive ? "Active" : "Disabled";
}

public static class UserService
{
    public static bool AnyUser()
    {
        using var db = DbFactory.Create();
        return db.Users.Any();
    }

    /// <summary>First start: create the administrator and the center name.</summary>
    public static void FirstSetup(string centerName, string fullName, string username, string password)
    {
        using var db = DbFactory.Create();
        if (db.Users.Any()) throw new UserException("Setup has already been completed.");
        if (string.IsNullOrWhiteSpace(centerName)) throw new UserException("Enter the hospital / vaccination center name.");
        CheckPassword(password);
        if (string.IsNullOrWhiteSpace(username)) throw new UserException("Enter a username.");
        db.Users.Add(new User { Username = username.Trim(), FullName = Validation.Clean(fullName) ?? username.Trim(), PasswordHash = PasswordHasher.Hash(password), Role = UserRole.Administrator });
        db.Settings.Add(new Setting { Key = "CenterName", Value = centerName.Trim() });
        Audit.Add(db, "Setup", "Users", username, "Administrator created");
        db.SaveChanges();
    }

    public static SessionUser Login(string username, string password)
    {
        using var db = DbFactory.Create();
        var user = db.Users.FirstOrDefault(u => u.Username == username.Trim());
        if (user is null || !PasswordHasher.Verify(user.PasswordHash, password)) throw new UserException("Wrong username or password.");
        if (!user.IsActive) throw new UserException("This user has been disabled by the administrator.");
        var s = new SessionUser(user.Id, user.Username, user.FullName, user.Role);
        Session.SignIn(s);
        Audit.Add(db, "Login", "Users", user.Id);
        db.SaveChanges();
        return s;
    }

    public static List<UserRow> List()
    {
        using var db = DbFactory.Create();
        return db.Users.OrderBy(u => u.Username).Select(u => new UserRow(u.Id, u.Username, u.FullName, u.Role, u.IsActive)).ToList();
    }

    public static void Save(int? id, string username, string fullName, UserRole role, bool active, string? password)
    {
        Session.Require(Module.Users);
        using var db = DbFactory.Create();
        if (string.IsNullOrWhiteSpace(username)) throw new UserException("Enter a username.");
        username = username.Trim();
        if (db.Users.Any(u => u.Username == username && u.Id != (id ?? 0))) throw new UserException("This username is already used.");
        User user;
        if (id is null)
        {
            CheckPassword(password);
            user = new User { PasswordHash = PasswordHasher.Hash(password!) };
            db.Users.Add(user);
        }
        else
        {
            user = db.Users.Find(id.Value) ?? throw new UserException("User not found.");
            if (!string.IsNullOrEmpty(password)) { CheckPassword(password); user.PasswordHash = PasswordHasher.Hash(password); }
        }
        user.Username = username;
        user.FullName = Validation.Clean(fullName) ?? username;
        user.Role = role;
        user.IsActive = active;

        var admins = db.Users.Where(u => u.Role == UserRole.Administrator && u.IsActive && u.Id != user.Id).Count()
                     + (user.Role == UserRole.Administrator && user.IsActive ? 1 : 0);
        if (admins == 0) throw new UserException("At least one active Administrator is required.");

        Audit.Add(db, id is null ? "Add" : "Update", "Users", username, $"Role {role}, active {active}");
        db.SaveChanges();
    }

    public static void ChangeOwnPassword(string current, string next)
    {
        var me = Session.User ?? throw new UserException("Not signed in.");
        using var db = DbFactory.Create();
        var user = db.Users.Find(me.Id)!;
        if (!PasswordHasher.Verify(user.PasswordHash, current)) throw new UserException("The current password is wrong.");
        CheckPassword(next);
        user.PasswordHash = PasswordHasher.Hash(next);
        Audit.Add(db, "Password", "Users", me.Id);
        db.SaveChanges();
    }

    private static void CheckPassword(string? p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < 6) throw new UserException("Password must have at least 6 characters.");
    }
}

public sealed record DoctorRow(int Id, string Name, string? Specialization, string? Hospital, string? ContactNumber, int Patients);

public static class DoctorService
{
    public static List<string> Names()
    {
        using var db = DbFactory.Create();
        return db.Doctors.Where(d => d.IsActive).OrderBy(d => d.Name).Select(d => d.Name).ToList();
    }

    public static List<DoctorRow> List(string? search = null)
    {
        using var db = DbFactory.Create();
        var q = db.Doctors.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(d => d.NormalizedName.Contains(s) || (d.Specialization ?? "").ToLower().Contains(s) || (d.Hospital ?? "").ToLower().Contains(s));
        }
        return q.OrderBy(d => d.Name)
            .Select(d => new DoctorRow(d.Id, d.Name, d.Specialization, d.Hospital, d.ContactNumber, db.Patients.Count(p => p.AdvisedByDoctorId == d.Id)))
            .ToList();
    }

    /// <summary>Returns the saved doctor with this name, or saves a new one. Names that differ only by "Dr.", case or spaces are the same doctor.</summary>
    public static Doctor? GetOrCreate(VaccinatorDbContext db, string? name)
    {
        var clean = Validation.Clean(name);
        if (clean is null) return null;
        var key = Doctor.Normalize(clean);
        var existing = db.Doctors.Local.FirstOrDefault(d => d.NormalizedName == key) ?? db.Doctors.FirstOrDefault(d => d.NormalizedName == key);
        if (existing is not null)
        {
            if (!existing.IsActive) existing.IsActive = true;
            return existing;
        }
        var doctor = new Doctor { Name = clean, NormalizedName = key };
        db.Doctors.Add(doctor);
        Audit.Add(db, "Add", "Doctors", clean, "Saved automatically from the Advised By field");
        return doctor;
    }

    public static void Save(int? id, string name, string? specialization, string? hospital, string? contact)
    {
        Session.Require(Module.Doctors);
        var clean = Validation.Clean(name) ?? throw new UserException("Enter the doctor's name.");
        using var db = DbFactory.Create();
        var key = Doctor.Normalize(clean);
        if (db.Doctors.Any(d => d.NormalizedName == key && d.Id != (id ?? 0))) throw new UserException("A doctor with this name is already saved.");
        var d = id is null ? new Doctor() : db.Doctors.Find(id.Value) ?? throw new UserException("Doctor not found.");
        if (id is null) db.Doctors.Add(d);
        d.Name = clean;
        d.NormalizedName = key;
        d.Specialization = Validation.Clean(specialization);
        d.Hospital = Validation.Clean(hospital);
        d.ContactNumber = Validation.Clean(contact);
        d.IsActive = true;
        Audit.Add(db, id is null ? "Add" : "Update", "Doctors", clean);
        db.SaveChanges();
    }

    /// <summary>Only the administrator deletes doctors. Patients keep the doctor's name as text.</summary>
    public static void Delete(int id)
    {
        Session.Require(Module.Settings);
        using var db = DbFactory.Create();
        var d = db.Doctors.Find(id) ?? throw new UserException("Doctor not found.");
        foreach (var p in db.Patients.Where(p => p.AdvisedByDoctorId == id)) { p.AdvisedByName ??= d.Name; p.AdvisedByDoctorId = null; }
        db.Doctors.Remove(d);
        Audit.Add(db, "Delete", "Doctors", d.Name);
        db.SaveChanges();
    }
}

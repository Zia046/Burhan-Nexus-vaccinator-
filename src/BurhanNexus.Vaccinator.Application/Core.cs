using System.Globalization;
using System.Text.RegularExpressions;
using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;

namespace BurhanNexus.Vaccinator.Application;

/// <summary>A friendly error for the user (validation, rule broken). Shown in a message box.</summary>
public sealed class UserException : Exception
{
    public UserException(string message) : base(message) { }
}

public enum Module
{
    Dashboard, Patients, Newborn, Vaccination, AntiRabies, Insulin, InsulinStock, Inventory,
    Appointments, Reports, Doctors, VaccineDatabase, Users, Settings, Backup
}

public static class Permissions
{
    public static bool Can(UserRole role, Module module) => role switch
    {
        UserRole.Administrator => true,
        UserRole.Vaccinator => module is Module.Dashboard or Module.Patients or Module.Newborn or Module.Vaccination or Module.AntiRabies
            or Module.Insulin or Module.Appointments or Module.Reports or Module.Doctors,
        UserRole.Receptionist => module is Module.Dashboard or Module.Patients or Module.Newborn or Module.Appointments or Module.Doctors,
        UserRole.InventoryManager => module is Module.Dashboard or Module.Inventory or Module.InsulinStock or Module.Insulin or Module.Reports,
        _ => false
    };

    public static string RoleName(UserRole role) => role switch
    {
        UserRole.InventoryManager => "Inventory Manager",
        _ => role.ToString()
    };
}

public sealed record SessionUser(int Id, string Username, string FullName, UserRole Role);

public static class Session
{
    public static SessionUser? User { get; private set; }
    public static void SignIn(SessionUser user) => User = user;
    public static void SignOut() => User = null;
    public static string Username => User?.Username ?? "system";
    public static string DisplayName => User?.FullName ?? "system";
    public static bool Can(Module m) => User is not null && Permissions.Can(User.Role, m);

    public static void Require(Module m)
    {
        if (!Can(m)) throw new UserException("Your user role is not allowed to do this. Ask the administrator.");
    }
}

/// <summary>Date and time display, following Settings → Date / Time format.</summary>
public static class Fmt
{
    public static string DateFormat { get; set; } = "dd-MM-yyyy";
    public static bool Use24Hour { get; set; }

    public static string D(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);
    public static string D(DateOnly? d) => d is null ? "" : D(d.Value);
    public static string D(DateTime d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);
    public static string T(TimeOnly t) => t.ToString(Use24Hour ? "HH:mm" : "hh:mm tt", CultureInfo.InvariantCulture);
    public static string DT(DateTime d) => d.ToString(DateFormat + (Use24Hour ? " HH:mm" : " hh:mm tt"), CultureInfo.InvariantCulture);
    public static string Money(decimal v) => "Rs " + v.ToString("#,0.##", CultureInfo.InvariantCulture);
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static string RouteName(AdministrationRoute r) => r switch
    {
        AdministrationRoute.IM => "IM (intramuscular)",
        AdministrationRoute.ID => "ID (intradermal)",
        AdministrationRoute.SC => "SC (subcutaneous)",
        AdministrationRoute.Oral => "Oral",
        _ => "Other"
    };

    public static string Category(PatientCategory c) => c switch
    {
        PatientCategory.PregnantWoman => "Pregnant Woman",
        PatientCategory.AntiRabies => "Anti-Rabies",
        PatientCategory.HealthcareWorker => "Healthcare Worker",
        _ => c.ToString()
    };
}

public static class Validation
{
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s.Trim(), @"\s+", " ");

    public static string Digits(string? s) => s is null ? "" : new string(s.Where(char.IsDigit).ToArray());

    /// <summary>CNIC: 13 digits, stored as 12345-1234567-1. Empty is allowed (newborns, people without CNIC).</summary>
    public static string? Cnic(string? input)
    {
        var d = Digits(input);
        if (d.Length == 0) return null;
        if (d.Length != 13) throw new UserException("CNIC must have 13 digits (for example 15101-1234567-1). Leave it empty if the patient has no CNIC.");
        return $"{d[..5]}-{d.Substring(5, 7)}-{d[12]}";
    }

    /// <summary>Pakistani mobile: 03XXXXXXXXX (11 digits). +92 / 92 is converted. Empty allowed.</summary>
    public static string? Mobile(string? input, string field = "Mobile number")
    {
        var d = Digits(input);
        if (d.Length == 0) return null;
        if (d.StartsWith("92") && d.Length == 12) d = "0" + d[2..];
        if (d.Length == 10 && d.StartsWith("3")) d = "0" + d;
        if (d.Length != 11 || !d.StartsWith("03")) throw new UserException($"{field} must be like 03XXXXXXXXX (11 digits).");
        return d;
    }
}

public static class Audit
{
    public static void Add(VaccinatorDbContext db, string action, string entity, object? id, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            At = DateTime.Now, UserId = Session.User?.Id, Username = Session.Username,
            Action = action, Entity = entity, EntityId = id?.ToString(), Details = details
        });
}

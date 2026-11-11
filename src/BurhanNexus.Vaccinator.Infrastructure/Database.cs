using System.Security.Cryptography;
using BurhanNexus.Vaccinator.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Infrastructure;

/// <summary>Where the program keeps its data. Everything is local: the software works without internet.</summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    /// <summary>For tests: put the data in a temporary folder.</summary>
    public static void UseRoot(string root) => _overrideRoot = root;

    public static string Root
    {
        get
        {
            var root = _overrideRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BurhanNexus", "Vaccinator");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string DatabaseFile => Path.Combine(Root, "vaccinator.db");
    public static string BackupFolder { get { var p = Path.Combine(Root, "backups"); Directory.CreateDirectory(p); return p; } }
    public static string LogFolder { get { var p = Path.Combine(Root, "logs"); Directory.CreateDirectory(p); return p; } }
    public static string PrintFolder { get { var p = Path.Combine(Path.GetTempPath(), "BurhanNexusVaccinator"); Directory.CreateDirectory(p); return p; } }
}

public static class DbFactory
{
    public static string ConnectionString(string? file = null) =>
        new SqliteConnectionStringBuilder { DataSource = file ?? AppPaths.DatabaseFile, ForeignKeys = true }.ToString();

    public static DbContextOptions<VaccinatorDbContext> Options(string? file = null) =>
        new DbContextOptionsBuilder<VaccinatorDbContext>().UseSqlite(ConnectionString(file)).Options;

    public static VaccinatorDbContext Create(string? file = null) => new(Options(file));

    /// <summary>Creates the tables on first start and loads the vaccine database. Safe to call every start.</summary>
    public static void Initialize(string? file = null)
    {
        using var db = Create(file);
        db.Database.EnsureCreated();
        using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            db.Database.OpenConnection();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }

        if (!db.Vaccines.Any())
        {
            db.Vaccines.AddRange(VaccineCatalog.Build());
            db.SaveChanges();
        }
    }
}

/// <summary>PBKDF2-SHA512 with a random salt (same scheme as Burhan Nexus LMS).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, 32);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string stored, string password)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(password)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var it) || it is < 1 or > 5_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>File backups of the SQLite database (consistent copy made by SQLite itself).</summary>
public static class BackupService
{
    public const string PendingFileName = "restore.pending";

    public static string Create(string targetPath, string? databaseFile = null)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(targetPath)) File.Delete(targetPath);
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile ?? AppPaths.DatabaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{targetPath.Replace("'", "''")}'";
        cmd.ExecuteNonQuery();
        return targetPath;
    }

    public static bool LooksLikeVaccinatorDatabase(string file)
    {
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Users','Patients','Vaccines','Appointments')";
            return Convert.ToInt32(cmd.ExecuteScalar()) == 4;
        }
        catch { return false; }
    }

    /// <summary>Restore is applied at the next program start, before the database is opened.</summary>
    public static void PrepareRestore(string backupFile)
    {
        if (!LooksLikeVaccinatorDatabase(backupFile)) throw new InvalidOperationException("This file is not a Burhan Nexus Vaccinator backup.");
        File.Copy(backupFile, Path.Combine(AppPaths.Root, PendingFileName), overwrite: true);
    }

    public static bool ApplyPendingRestore()
    {
        var pending = Path.Combine(AppPaths.Root, PendingFileName);
        if (!File.Exists(pending)) return false;
        SqliteConnection.ClearAllPools();
        var db = AppPaths.DatabaseFile;
        if (File.Exists(db)) File.Copy(db, Path.Combine(AppPaths.BackupFolder, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db"), true);
        foreach (var extra in new[] { db + "-wal", db + "-shm" }) if (File.Exists(extra)) File.Delete(extra);
        File.Move(pending, db, true);
        return true;
    }

    /// <summary>One automatic backup per day; the newest 30 are kept.</summary>
    public static void AutomaticDailyBackup()
    {
        if (!File.Exists(AppPaths.DatabaseFile)) return;
        var target = Path.Combine(AppPaths.BackupFolder, $"auto-{DateTime.Now:yyyyMMdd}.db");
        if (File.Exists(target)) return;
        Create(target);
        foreach (var old in Directory.GetFiles(AppPaths.BackupFolder, "auto-*.db").OrderByDescending(f => f).Skip(30)) File.Delete(old);
    }
}

using BurhanNexus.Vaccinator.Domain;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Infrastructure;

public sealed class VaccinatorDbContext : DbContext
{
    public VaccinatorDbContext(DbContextOptions<VaccinatorDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<NewbornDetail> NewbornPatients => Set<NewbornDetail>();
    public DbSet<Vaccine> Vaccines => Set<Vaccine>();
    public DbSet<VaccineProtocol> VaccineProtocols => Set<VaccineProtocol>();
    public DbSet<ProtocolDose> VaccineDoses => Set<ProtocolDose>();
    public DbSet<PatientCourse> PatientCourses => Set<PatientCourse>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<VaccinationRecord> VaccinationRecords => Set<VaccinationRecord>();
    public DbSet<AntiRabiesCase> AntiRabiesPatients => Set<AntiRabiesCase>();
    public DbSet<VaccineBatch> VaccineInventory => Set<VaccineBatch>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<ColdChainLog> ColdChainLogs => Set<ColdChainLog>();
    public DbSet<InsulinProduct> InsulinProducts => Set<InsulinProduct>();
    public DbSet<InsulinBatch> InsulinBatches => Set<InsulinBatch>();
    public DbSet<InsulinTransaction> InsulinTransactions => Set<InsulinTransaction>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(60).UseCollation("NOCASE");
        });

        b.Entity<Setting>(e => { e.ToTable("Settings"); e.HasKey(x => x.Key); });
        b.Entity<AuditLog>(e => { e.ToTable("AuditLogs"); e.HasIndex(x => x.At); });

        b.Entity<Doctor>(e =>
        {
            e.ToTable("Doctors");
            e.HasIndex(x => x.NormalizedName).IsUnique();
        });

        b.Entity<Patient>(e =>
        {
            e.ToTable("Patients");
            e.HasIndex(x => x.PatientNo).IsUnique();
            e.HasIndex(x => x.Cnic);
            e.HasIndex(x => x.Mobile);
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).UseCollation("NOCASE");
            e.Property(x => x.GuardianName).UseCollation("NOCASE");
            e.HasOne(x => x.AdvisedByDoctor).WithMany().HasForeignKey(x => x.AdvisedByDoctorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Newborn).WithOne(x => x.Patient).HasForeignKey<NewbornDetail>(x => x.PatientId);
        });

        b.Entity<NewbornDetail>(e => { e.ToTable("NewbornPatients"); e.HasKey(x => x.PatientId); });

        b.Entity<Vaccine>(e => { e.ToTable("Vaccines"); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<VaccineProtocol>(e =>
        {
            e.ToTable("VaccineProtocols");
            e.HasOne(x => x.Vaccine).WithMany(x => x.Protocols).HasForeignKey(x => x.VaccineId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProtocolDose>(e =>
        {
            e.ToTable("VaccineDoses");
            e.HasOne(x => x.Protocol).WithMany(x => x.Doses).HasForeignKey(x => x.ProtocolId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PatientCourse>(e =>
        {
            e.ToTable("PatientCourses");
            e.HasOne(x => x.Patient).WithMany(x => x.Courses).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Protocol).WithMany().HasForeignKey(x => x.ProtocolId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Appointment>(e =>
        {
            e.ToTable("Appointments");
            e.HasIndex(x => new { x.Status, x.DueDate });
            e.HasOne(x => x.Course).WithMany(x => x.Appointments).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vaccine).WithMany().HasForeignKey(x => x.VaccineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ProtocolDose).WithMany().HasForeignKey(x => x.ProtocolDoseId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VaccinationRecord>(e =>
        {
            e.ToTable("VaccinationRecords");
            e.HasIndex(x => x.RecordNo).IsUnique();
            e.HasIndex(x => x.AdministrationDate);
            e.HasOne(x => x.Patient).WithMany(x => x.Vaccinations).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vaccine).WithMany().HasForeignKey(x => x.VaccineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AntiRabiesCase>(e =>
        {
            e.ToTable("AntiRabiesPatients");
            e.HasIndex(x => x.CaseNo).IsUnique();
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Protocol).WithMany().HasForeignKey(x => x.ProtocolId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VaccineBatch>(e =>
        {
            e.ToTable("VaccineInventory");
            e.HasOne(x => x.Vaccine).WithMany().HasForeignKey(x => x.VaccineId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InventoryTransaction>(e =>
        {
            e.ToTable("InventoryTransactions");
            e.HasIndex(x => x.TransactionNo).IsUnique();
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ColdChainLog>(e => { e.ToTable("ColdChainLogs"); e.Ignore(x => x.IsExcursion); });

        b.Entity<InsulinProduct>(e => e.ToTable("InsulinProducts"));
        b.Entity<InsulinBatch>(e =>
        {
            e.ToTable("InsulinBatches");
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InsulinTransaction>(e =>
        {
            e.ToTable("InsulinTransactions");
            e.HasIndex(x => x.TransactionNo).IsUnique();
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PrintLog>(e => e.ToTable("PrintLogs"));
    }
}

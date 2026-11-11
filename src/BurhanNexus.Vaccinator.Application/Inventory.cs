using BurhanNexus.Vaccinator.Domain;
using BurhanNexus.Vaccinator.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BurhanNexus.Vaccinator.Application;

public sealed record BatchRow(int Id, int VaccineId, string Vaccine, string? Brand, string? Manufacturer, string BatchNumber, int Received, int Available,
    string? VialSize, DateOnly Expiry, DateOnly? Manufactured, string? Supplier, DateOnly? Purchased, string? Storage, VvmStatus Vvm, string? Location,
    bool Quarantined, int LowStockLevel, string Status)
{
    public string ExpiryText => Fmt.D(Expiry);
    public string ManufacturedText => Fmt.D(Manufactured);
    public string PurchasedText => Fmt.D(Purchased);
    public bool IsProblem => Status != "OK";
    public int DaysToExpiry => Expiry.DayNumber - Fmt.Today.DayNumber;
}

public sealed class BatchInput
{
    public int VaccineId { get; set; }
    public string? Brand { get; set; }
    public string? Manufacturer { get; set; }
    public string BatchNumber { get; set; } = "";
    public int Quantity { get; set; }
    public string? VialSize { get; set; }
    public DateOnly ExpiryDate { get; set; } = Fmt.Today.AddMonths(12);
    public DateOnly? ManufacturingDate { get; set; }
    public string? Supplier { get; set; }
    public DateOnly? PurchaseDate { get; set; } = Fmt.Today;
    public string? StorageTemperature { get; set; }
    public VvmStatus Vvm { get; set; } = VvmStatus.Stage1;
    public string? StockLocation { get; set; }
    public int LowStockLevel { get; set; } = 10;
}

public sealed record InventoryTxRow(DateTime At, string TransactionNo, string Vaccine, string Batch, string Type, int Quantity, string? Reason, string Username)
{
    public string AtText => Fmt.DT(At);
}

public sealed record ColdChainRow(DateTime At, string Location, decimal TemperatureC, string RecordedBy, string? Remarks, bool Excursion)
{
    public string AtText => Fmt.DT(At);
    public string Status => Excursion ? "EXCURSION – outside +2 to +8 °C" : "OK";
}

public static class InventoryService
{
    public const int NearExpiryDays = 30;

    public static string StatusOf(VaccineBatch b)
    {
        var today = Fmt.Today;
        if (b.IsExpired(today)) return "EXPIRED";
        if (b.IsQuarantined) return "QUARANTINED";
        if (b.Vvm is VvmStatus.Stage3 or VvmStatus.Stage4) return "VVM DISCARD";
        if (b.QuantityAvailable <= 0) return "OUT OF STOCK";
        if (b.ExpiryDate.DayNumber - today.DayNumber <= NearExpiryDays) return "EXPIRING SOON";
        if (b.QuantityAvailable <= b.LowStockLevel) return "LOW STOCK";
        return "OK";
    }

    public static List<BatchRow> Batches(string? search = null, bool onlyProblems = false)
    {
        using var db = DbFactory.Create();
        var rows = db.VaccineInventory.AsNoTracking().Include(b => b.Vaccine).OrderBy(b => b.Vaccine!.Name).ThenBy(b => b.ExpiryDate).ToList()
            .Select(b => new BatchRow(b.Id, b.VaccineId, b.Vaccine!.Name, b.Brand, b.Manufacturer, b.BatchNumber, b.QuantityReceived, b.QuantityAvailable, b.VialSize,
                b.ExpiryDate, b.ManufacturingDate, b.Supplier, b.PurchaseDate, b.StorageTemperature, b.Vvm, b.StockLocation, b.IsQuarantined, b.LowStockLevel, StatusOf(b)))
            .ToList();
        var s = Validation.Clean(search)?.ToLower();
        if (s is not null) rows = rows.Where(r => r.Vaccine.ToLower().Contains(s) || r.BatchNumber.ToLower().Contains(s) || (r.Brand ?? "").ToLower().Contains(s)).ToList();
        if (onlyProblems) rows = rows.Where(r => r.IsProblem && !(r.Status == "OUT OF STOCK")).ToList();
        return rows;
    }

    public static List<BatchOption> UsableBatches(int vaccineId, DateOnly onDate)
    {
        using var db = DbFactory.Create();
        return db.VaccineInventory.AsNoTracking().Where(b => b.VaccineId == vaccineId && !b.IsQuarantined && b.QuantityAvailable > 0 && b.ExpiryDate >= onDate)
            .OrderBy(b => b.ExpiryDate).ToList()
            .Where(b => b.WhyNotUsable(onDate) is null)
            .Select(b => new BatchOption(b.Id, b.BatchNumber, b.Brand, b.ExpiryDate, b.QuantityAvailable)).ToList();
    }

    public static void StockIn(BatchInput i)
    {
        Session.Require(Module.Inventory);
        var number = Validation.Clean(i.BatchNumber) ?? throw new UserException("Enter the batch number.");
        if (i.Quantity <= 0) throw new UserException("Enter the quantity received (number of doses).");
        if (i.ExpiryDate < Fmt.Today) throw new UserException("This batch has already expired. Expired stock cannot be received into usable stock.");
        if (i.ManufacturingDate is DateOnly m && m > i.ExpiryDate) throw new UserException("Manufacturing date is after the expiry date.");
        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var batch = db.VaccineInventory.FirstOrDefault(b => b.VaccineId == i.VaccineId && b.BatchNumber == number);
        if (batch is null)
        {
            batch = new VaccineBatch { VaccineId = i.VaccineId, BatchNumber = number };
            db.VaccineInventory.Add(batch);
        }
        else if (batch.ExpiryDate != i.ExpiryDate)
            throw new UserException($"Batch {number} is already saved with expiry {Fmt.D(batch.ExpiryDate)}. Check the expiry date.");

        batch.Brand = Validation.Clean(i.Brand) ?? batch.Brand;
        batch.Manufacturer = Validation.Clean(i.Manufacturer) ?? batch.Manufacturer;
        batch.VialSize = Validation.Clean(i.VialSize) ?? batch.VialSize;
        batch.ExpiryDate = i.ExpiryDate;
        batch.ManufacturingDate = i.ManufacturingDate ?? batch.ManufacturingDate;
        batch.Supplier = Validation.Clean(i.Supplier) ?? batch.Supplier;
        batch.PurchaseDate = i.PurchaseDate ?? batch.PurchaseDate;
        batch.StorageTemperature = Validation.Clean(i.StorageTemperature) ?? batch.StorageTemperature ?? db.Vaccines.Find(i.VaccineId)?.StorageTemperature;
        batch.Vvm = i.Vvm;
        batch.StockLocation = Validation.Clean(i.StockLocation) ?? batch.StockLocation;
        batch.LowStockLevel = Math.Max(0, i.LowStockLevel);
        batch.QuantityReceived += i.Quantity;
        batch.QuantityAvailable += i.Quantity;
        db.SaveChanges();
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            TransactionNo = $"IT-{DateTime.Now:yyyyMMddHHmmssfff}-{batch.Id}", BatchId = batch.Id, Type = InventoryTransactionType.StockIn, Quantity = i.Quantity,
            Reason = Validation.Clean(i.Supplier), Username = Session.Username
        });
        Audit.Add(db, "Stock in", "VaccineInventory", number, $"+{i.Quantity}");
        db.SaveChanges();
        tx.Commit();
    }

    /// <summary>Stock out (transfer/return), wastage or count correction. Quantity is always positive here.</summary>
    public static void Remove(int batchId, int quantity, InventoryTransactionType type, string reason)
    {
        Session.Require(Module.Inventory);
        if (quantity <= 0) throw new UserException("Enter a quantity greater than zero.");
        if (string.IsNullOrWhiteSpace(reason)) throw new UserException("Write the reason.");
        if (type is not (InventoryTransactionType.StockOut or InventoryTransactionType.Wastage or InventoryTransactionType.Adjustment or InventoryTransactionType.Expired))
            throw new UserException("Wrong transaction type.");
        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var b = db.VaccineInventory.Find(batchId) ?? throw new UserException("Batch not found.");
        if (quantity > b.QuantityAvailable) throw new UserException($"Only {b.QuantityAvailable} doses are available in this batch.");
        if (type == InventoryTransactionType.StockOut && b.WhyNotUsable(Fmt.Today) is string why && b.QuantityAvailable > 0 && (b.IsExpired(Fmt.Today) || b.IsQuarantined))
            throw new UserException($"Cannot issue this batch: {why}. Record it as Wastage / Expired disposal instead.");
        b.QuantityAvailable -= quantity;
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            TransactionNo = $"IT-{DateTime.Now:yyyyMMddHHmmssfff}-{b.Id}", BatchId = b.Id, Type = type, Quantity = -quantity, Reason = reason.Trim(), Username = Session.Username
        });
        Audit.Add(db, type.ToString(), "VaccineInventory", b.BatchNumber, $"-{quantity}: {reason}");
        db.SaveChanges();
        tx.Commit();
    }

    public static void SetQuarantine(int batchId, bool quarantined, string? reason)
    {
        Session.Require(Module.Inventory);
        using var db = DbFactory.Create();
        var b = db.VaccineInventory.Find(batchId) ?? throw new UserException("Batch not found.");
        b.IsQuarantined = quarantined;
        b.QuarantineReason = quarantined ? Validation.Clean(reason) : null;
        Audit.Add(db, quarantined ? "Quarantine" : "Release", "VaccineInventory", b.BatchNumber, reason);
        db.SaveChanges();
    }

    public static void SetVvm(int batchId, VvmStatus vvm)
    {
        Session.Require(Module.Inventory);
        using var db = DbFactory.Create();
        var b = db.VaccineInventory.Find(batchId) ?? throw new UserException("Batch not found.");
        b.Vvm = vvm;
        Audit.Add(db, "VVM", "VaccineInventory", b.BatchNumber, vvm.ToString());
        db.SaveChanges();
    }

    public static List<InventoryTxRow> Transactions(DateOnly? from = null, DateOnly? to = null)
    {
        using var db = DbFactory.Create();
        var q = db.InventoryTransactions.AsNoTracking().Include(t => t.Batch).ThenInclude(b => b!.Vaccine).AsQueryable();
        if (from is DateOnly f) { var fd = f.ToDateTime(TimeOnly.MinValue); q = q.Where(t => t.At >= fd); }
        if (to is DateOnly t2) { var td = t2.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(t => t.At < td); }
        return q.OrderByDescending(t => t.Id).Take(3000).ToList()
            .Select(t => new InventoryTxRow(t.At, t.TransactionNo, t.Batch!.Vaccine!.Name, t.Batch.BatchNumber, t.Type.ToString(), t.Quantity, t.Reason, t.Username)).ToList();
    }

    public static void AddTemperature(string location, decimal tempC, string? remarks)
    {
        Session.Require(Module.Inventory);
        if (string.IsNullOrWhiteSpace(location)) throw new UserException("Enter the refrigerator / cold box name.");
        if (tempC is < -40m or > 60m) throw new UserException("The temperature looks wrong.");
        using var db = DbFactory.Create();
        db.ColdChainLogs.Add(new ColdChainLog { RecordedAt = DateTime.Now, Location = location.Trim(), TemperatureC = tempC, RecordedBy = Session.DisplayName, Remarks = Validation.Clean(remarks) });
        if (tempC < 2m || tempC > 8m) Audit.Add(db, "Temperature excursion", "ColdChainLogs", location, $"{tempC} °C");
        db.SaveChanges();
    }

    public static List<ColdChainRow> Temperatures(int max = 500)
    {
        using var db = DbFactory.Create();
        return db.ColdChainLogs.AsNoTracking().OrderByDescending(c => c.Id).Take(max).ToList()
            .Select(c => new ColdChainRow(c.RecordedAt, c.Location, c.TemperatureC, c.RecordedBy, c.Remarks, c.IsExcursion)).ToList();
    }
}

// ============================================================================ Insulin

public sealed record InsulinProductRow(int Id, string Brand, string InsulinType, string Strength, string VialSize, decimal UnitPrice, int LowStockLevel, int Available, string? Storage)
{
    public string Display => $"{Brand} {InsulinType} {Strength} ({VialSize})";
    public string PriceText => Fmt.Money(UnitPrice);
    public bool Low => Available <= LowStockLevel;
    public override string ToString() => Display;
}

public sealed record InsulinBatchRow(int Id, int ProductId, string Product, string BatchNumber, DateOnly? Manufactured, DateOnly Expiry, int Received, int Available,
    string? Location, decimal? TemperatureC, string Status)
{
    public string ExpiryText => Fmt.D(Expiry);
    public string Display => $"{BatchNumber} · Exp {Fmt.D(Expiry)} · {Available} vial(s)";
    public override string ToString() => Display;
}

public sealed class InsulinIssueInput
{
    public int PatientId { get; set; }
    public int BatchId { get; set; }
    public int Vials { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public DateOnly IssueDate { get; set; } = Fmt.Today;
    public string? PrescribingDoctor { get; set; }
    public string? PrescribedDosage { get; set; }
    public string? Remarks { get; set; }
}

public sealed class InsulinReceipt
{
    public string TransactionNo { get; init; } = "";
    public DateTime At { get; init; }
    public string PatientNo { get; init; } = "";
    public string PatientName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string? Mobile { get; init; }
    public string? Cnic { get; init; }
    public string Product { get; init; } = "";
    public string InsulinType { get; init; } = "";
    public string Strength { get; init; } = "";
    public string VialSize { get; init; } = "";
    public string Batch { get; init; } = "";
    public DateOnly Expiry { get; init; }
    public int Vials { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
    public string? Doctor { get; init; }
    public string? Dosage { get; init; }
    public string? Remarks { get; init; }
    public string IssuedBy { get; init; } = "";
}

public static class InsulinService
{
    public static string StatusOf(InsulinBatch b, int lowLevel)
    {
        var today = Fmt.Today;
        if (b.ExpiryDate < today) return "EXPIRED";
        if (b.QuantityAvailable <= 0) return "OUT OF STOCK";
        if (b.ExpiryDate.DayNumber - today.DayNumber <= InventoryService.NearExpiryDays) return "EXPIRING SOON";
        return "OK";
    }

    public static List<InsulinProductRow> Products(bool activeOnly = true)
    {
        using var db = DbFactory.Create();
        var stock = db.InsulinBatches.Where(b => b.QuantityAvailable > 0).ToList().Where(b => b.ExpiryDate >= Fmt.Today)
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QuantityAvailable));
        return db.InsulinProducts.Where(p => !activeOnly || p.IsActive).OrderBy(p => p.Brand).ToList()
            .Select(p => new InsulinProductRow(p.Id, p.Brand, p.InsulinType, p.Strength, p.VialSize, p.UnitPrice, p.LowStockLevel,
                stock.TryGetValue(p.Id, out var s) ? s : 0, p.StorageInstructions)).ToList();
    }

    public static void SaveProduct(int? id, string brand, string type, string strength, string vialSize, decimal price, int lowLevel, string? storage)
    {
        Session.Require(Module.InsulinStock);
        if (string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(type)) throw new UserException("Enter the insulin brand and type.");
        if (price < 0) throw new UserException("Price cannot be negative.");
        using var db = DbFactory.Create();
        var p = id is null ? new InsulinProduct() : db.InsulinProducts.Find(id.Value) ?? throw new UserException("Product not found.");
        if (id is null) db.InsulinProducts.Add(p);
        p.Brand = brand.Trim(); p.InsulinType = type.Trim(); p.Strength = strength?.Trim() ?? ""; p.VialSize = vialSize?.Trim() ?? "";
        p.UnitPrice = price; p.LowStockLevel = Math.Max(0, lowLevel); p.StorageInstructions = Validation.Clean(storage) ?? "+2 °C to +8 °C. Do not freeze.";
        Audit.Add(db, id is null ? "Add" : "Update", "InsulinProducts", p.Brand);
        db.SaveChanges();
    }

    public static List<InsulinBatchRow> Batches(int? productId = null, bool usableOnly = false)
    {
        using var db = DbFactory.Create();
        var q = db.InsulinBatches.AsNoTracking().Include(b => b.Product).AsQueryable();
        if (productId is int pid) q = q.Where(b => b.ProductId == pid);
        var list = q.OrderBy(b => b.ExpiryDate).ToList();
        if (usableOnly) list = list.Where(b => b.QuantityAvailable > 0 && b.ExpiryDate >= Fmt.Today).ToList();
        return list.Select(b => new InsulinBatchRow(b.Id, b.ProductId, $"{b.Product!.Brand} {b.Product.InsulinType} {b.Product.Strength}", b.BatchNumber,
            b.ManufacturingDate, b.ExpiryDate, b.QuantityReceived, b.QuantityAvailable, b.StorageLocation, b.StorageTemperatureC, StatusOf(b, b.Product.LowStockLevel))).ToList();
    }

    public static void StockIn(int productId, string batchNumber, DateOnly? mfg, DateOnly expiry, int vials, string? location, decimal? tempC)
    {
        Session.Require(Module.InsulinStock);
        var number = Validation.Clean(batchNumber) ?? throw new UserException("Enter the batch number.");
        if (vials <= 0) throw new UserException("Enter the number of vials received.");
        if (expiry < Fmt.Today) throw new UserException("This batch has already expired.");
        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var b = db.InsulinBatches.FirstOrDefault(x => x.ProductId == productId && x.BatchNumber == number);
        if (b is null) { b = new InsulinBatch { ProductId = productId, BatchNumber = number, ExpiryDate = expiry }; db.InsulinBatches.Add(b); }
        else if (b.ExpiryDate != expiry) throw new UserException($"Batch {number} is already saved with expiry {Fmt.D(b.ExpiryDate)}.");
        b.ManufacturingDate = mfg ?? b.ManufacturingDate;
        b.QuantityReceived += vials;
        b.QuantityAvailable += vials;
        b.StorageLocation = Validation.Clean(location) ?? b.StorageLocation;
        b.StorageTemperatureC = tempC ?? b.StorageTemperatureC;
        db.SaveChanges();
        var product = db.InsulinProducts.Find(productId)!;
        db.InsulinTransactions.Add(new InsulinTransaction
        {
            TransactionNo = $"INS-IN-{DateTime.Now:yyyyMMddHHmmssfff}", Type = InsulinTransactionType.StockIn, BatchId = b.Id, IssueDate = Fmt.Today,
            Quantity = vials, UnitPrice = product.UnitPrice, TotalAmount = 0, Username = Session.Username
        });
        Audit.Add(db, "Stock in", "InsulinBatches", number, $"+{vials}");
        db.SaveChanges();
        tx.Commit();
    }

    /// <summary>Issues vials to a patient. The prescribed dosage is stored exactly as written by the prescriber – it is never calculated.</summary>
    public static long Issue(InsulinIssueInput i)
    {
        Session.Require(Module.Insulin);
        if (i.Vials <= 0) throw new UserException("Enter the number of vials.");
        if (i.IssueDate > Fmt.Today) throw new UserException("Issue date cannot be in the future.");
        if (i.UnitPrice < 0) throw new UserException("Price cannot be negative.");
        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var patient = db.Patients.Find(i.PatientId) ?? throw new UserException("Select the patient.");
        var b = db.InsulinBatches.Include(x => x.Product).FirstOrDefault(x => x.Id == i.BatchId) ?? throw new UserException("Select the batch.");
        if (b.ExpiryDate < i.IssueDate) throw new UserException("This batch has expired and cannot be issued.");
        if (i.Vials > b.QuantityAvailable) throw new UserException($"Only {b.QuantityAvailable} vial(s) available in this batch.");
        var doctor = DoctorService.GetOrCreate(db, i.PrescribingDoctor);
        b.QuantityAvailable -= i.Vials;
        var t = new InsulinTransaction
        {
            TransactionNo = "TMP-" + Guid.NewGuid().ToString("N"), Type = InsulinTransactionType.Issue, BatchId = b.Id, PatientId = patient.Id, IssueDate = i.IssueDate,
            Quantity = -i.Vials, UnitPrice = i.UnitPrice, TotalAmount = i.UnitPrice * i.Vials, PrescribingDoctor = doctor?.Name,
            PrescribedDosage = Validation.Clean(i.PrescribedDosage), Remarks = Validation.Clean(i.Remarks), Username = Session.Username
        };
        db.InsulinTransactions.Add(t);
        db.SaveChanges();
        t.TransactionNo = $"INS-{t.Id:D6}";
        if (patient.Category == PatientCategory.General) patient.Category = PatientCategory.Insulin;
        Audit.Add(db, "Issue insulin", "InsulinTransactions", t.TransactionNo, $"{patient.PatientNo} {i.Vials} vial(s) {b.Product!.Brand} batch {b.BatchNumber}");
        db.SaveChanges();
        tx.Commit();
        return t.Id;
    }

    public static void Wastage(int batchId, int vials, string reason)
    {
        Session.Require(Module.InsulinStock);
        if (vials <= 0 || string.IsNullOrWhiteSpace(reason)) throw new UserException("Enter the vials and the reason.");
        using var db = DbFactory.Create();
        var b = db.InsulinBatches.Find(batchId) ?? throw new UserException("Batch not found.");
        if (vials > b.QuantityAvailable) throw new UserException($"Only {b.QuantityAvailable} vial(s) available.");
        b.QuantityAvailable -= vials;
        db.InsulinTransactions.Add(new InsulinTransaction
        {
            TransactionNo = $"INS-W-{DateTime.Now:yyyyMMddHHmmssfff}", Type = InsulinTransactionType.Wastage, BatchId = b.Id, IssueDate = Fmt.Today, Quantity = -vials,
            Remarks = reason.Trim(), Username = Session.Username
        });
        Audit.Add(db, "Insulin wastage", "InsulinBatches", b.BatchNumber, $"-{vials}: {reason}");
        db.SaveChanges();
    }

    public static List<InsulinRow> Transactions(VaccinatorDbContext db, int? patientId = null, DateOnly? from = null, DateOnly? to = null, bool issuesOnly = true)
    {
        var q = db.InsulinTransactions.AsNoTracking().Include(t => t.Patient).Include(t => t.Batch).ThenInclude(b => b!.Product).AsQueryable();
        if (issuesOnly) q = q.Where(t => t.Type == InsulinTransactionType.Issue);
        if (patientId is int pid) q = q.Where(t => t.PatientId == pid);
        if (from is DateOnly f) q = q.Where(t => t.IssueDate >= f);
        if (to is DateOnly t2) q = q.Where(t => t.IssueDate <= t2);
        return q.OrderByDescending(t => t.Id).Take(5000).ToList()
            .Select(t => new InsulinRow(t.Id, t.TransactionNo, t.IssueDate, t.Patient?.PatientNo, t.Patient?.Name,
                $"{t.Batch!.Product!.Brand} {t.Batch.Product.InsulinType} {t.Batch.Product.Strength}", t.Batch.BatchNumber, Math.Abs(t.Quantity), t.UnitPrice, t.TotalAmount,
                t.PrescribingDoctor, t.PrescribedDosage, t.Type.ToString())).ToList();
    }

    public static List<InsulinRow> Transactions(DateOnly? from = null, DateOnly? to = null, bool issuesOnly = true)
    {
        using var db = DbFactory.Create();
        return Transactions(db, null, from, to, issuesOnly);
    }

    public static InsulinReceipt Receipt(long transactionId)
    {
        using var db = DbFactory.Create();
        var t = db.InsulinTransactions.AsNoTracking().Include(x => x.Patient).Include(x => x.Batch).ThenInclude(b => b!.Product).First(x => x.Id == transactionId);
        var p = t.Batch!.Product!;
        return new InsulinReceipt
        {
            TransactionNo = t.TransactionNo, At = t.At, PatientNo = t.Patient?.PatientNo ?? "", PatientName = t.Patient?.Name ?? "", GuardianName = t.Patient?.GuardianName,
            Mobile = t.Patient?.Mobile, Cnic = t.Patient?.Cnic, Product = p.Brand, InsulinType = p.InsulinType, Strength = p.Strength, VialSize = p.VialSize,
            Batch = t.Batch.BatchNumber, Expiry = t.Batch.ExpiryDate, Vials = Math.Abs(t.Quantity), UnitPrice = t.UnitPrice, Total = t.TotalAmount,
            Doctor = t.PrescribingDoctor, Dosage = t.PrescribedDosage, Remarks = t.Remarks, IssuedBy = t.Username
        };
    }
}

// ============================================================================ Anti-Rabies

public sealed class RabiesInput
{
    public int? ExistingPatientId { get; set; }
    public PatientInput NewPatient { get; set; } = new() { Category = PatientCategory.AntiRabies };
    public DateOnly BiteDate { get; set; } = Fmt.Today;
    public DateOnly FirstVaccineDate { get; set; } = Fmt.Today;
    public AnimalType AnimalType { get; set; }
    public string? AnimalOther { get; set; }
    public BiteCategory Category { get; set; } = BiteCategory.II;
    public string? BiteLocation { get; set; }
    public bool PreviouslyVaccinated { get; set; }
    public string? PreviousVaccinationDetails { get; set; }
    public string? VaccineBrand { get; set; }
    public int ProtocolId { get; set; }
    public bool WoundWashed { get; set; }
    public RigType Rig { get; set; }
    public string? RigDose { get; set; }
    public string? RigBatch { get; set; }
    public DateOnly? RigDate { get; set; }
    public bool Immunocompromised { get; set; }
    public string? AdvisedBy { get; set; }
    public string? Remarks { get; set; }
}

public enum RabiesList { All, FirstDosePending, SecondDosePending, ThirdDosePending, FourthDosePending, DueToday, Upcoming, Overdue, Completed }

public static class RabiesService
{
    /// <summary>Safety checks shown to the user before saving. They never decide the treatment.</summary>
    public static List<string> Warnings(RabiesInput i)
    {
        var w = new List<string>();
        using var db = DbFactory.Create();
        var p = db.VaccineProtocols.Find(i.ProtocolId);
        if (!i.WoundWashed) w.Add("REMINDER: wash the wound immediately with soap and running water for about 15 minutes.");
        if (i.Category == BiteCategory.III)
            w.Add("CATEGORY III EXPOSURE: professional assessment is required. Rabies immunoglobulin (RIG) is indicated for category III exposures in patients not previously vaccinated.");
        if (i.Category == BiteCategory.III && !i.PreviouslyVaccinated && i.Rig == RigType.None)
            w.Add("No RIG has been recorded for this category III case. Record RIG if given, or the reason in Remarks.");
        if (i.Immunocompromised) w.Add("IMMUNOCOMPROMISED patient: flagged for clinical review. Protocol and RIG must be decided by a healthcare professional.");
        if (i.Category == BiteCategory.I) w.Add("Category I (touching/feeding, licks on intact skin) normally needs no PEP. Confirm with the doctor before starting a course.");
        if (p is not null)
        {
            if (i.PreviouslyVaccinated && p.Kind is ProtocolKind.RabiesPepIntramuscular or ProtocolKind.RabiesPepIntradermal)
                w.Add("Patient is marked PREVIOUSLY VACCINATED but a full PEP protocol is selected. Previously vaccinated patients usually need a shorter booster protocol without RIG.");
            if (!i.PreviouslyVaccinated && p.Kind == ProtocolKind.RabiesPepPreviouslyVaccinated)
                w.Add("The selected protocol is ONLY for previously vaccinated patients, but the patient is not marked as previously vaccinated.");
            if (p.Kind == ProtocolKind.RabiesPreExposure) w.Add("A PRE-EXPOSURE protocol is selected for an animal bite case. Check the protocol.");
            if (!p.ApprovedByCenter) w.Add("This protocol has not yet been approved by your center's medical officer (Settings → Vaccine Protocols).");
            if (i.Immunocompromised && p.Kind != ProtocolKind.RabiesSpecialCategory) w.Add("For immunocompromised patients a special protocol may be needed (clinical decision).");
        }
        if (i.FirstVaccineDate < i.BiteDate) w.Add("The first vaccine date is before the bite date.");
        if (i.FirstVaccineDate.DayNumber - i.BiteDate.DayNumber > 7) w.Add("PEP is starting more than 7 days after the bite. Start it as soon as possible – a delayed start is still indicated.");
        return w;
    }

    public static (int CaseId, int PatientId) Register(RabiesInput i, bool allowSimilarPatient = false)
    {
        Session.Require(Module.AntiRabies);
        if (i.BiteDate > Fmt.Today) throw new UserException("The bite date cannot be in the future.");
        if (i.FirstVaccineDate < i.BiteDate) throw new UserException("The first vaccine date cannot be before the bite date.");
        if (i.ProtocolId == 0) throw new UserException("Select the protocol. A healthcare professional must choose the correct protocol.");

        int patientId;
        if (i.ExistingPatientId is int existing) patientId = existing;
        else
        {
            i.NewPatient.Category = PatientCategory.AntiRabies;
            i.NewPatient.AdvisedBy ??= i.AdvisedBy;
            i.NewPatient.IsImmunocompromised = i.Immunocompromised;
            patientId = PatientService.Register(i.NewPatient, allowSimilarPatient);
        }

        using var db = DbFactory.Create();
        using var tx = db.Database.BeginTransaction();
        var patient = db.Patients.Find(patientId)!;
        if (i.Immunocompromised) patient.IsImmunocompromised = true;
        var doctor = DoctorService.GetOrCreate(db, i.AdvisedBy);
        var protocol = db.VaccineProtocols.Find(i.ProtocolId)!;
        var c = new AntiRabiesCase
        {
            CaseNo = "TMP-" + Guid.NewGuid().ToString("N"), PatientId = patientId, BiteDate = i.BiteDate, FirstVaccineDate = i.FirstVaccineDate, AnimalType = i.AnimalType,
            AnimalOther = Validation.Clean(i.AnimalOther), Category = i.Category, BiteLocation = Validation.Clean(i.BiteLocation), PreviouslyVaccinated = i.PreviouslyVaccinated,
            PreviousVaccinationDetails = Validation.Clean(i.PreviousVaccinationDetails), VaccineBrand = Validation.Clean(i.VaccineBrand),
            Route = protocol.Kind == ProtocolKind.RabiesPepIntradermal || protocol.Name.Contains(" ID ") ? AdministrationRoute.ID : AdministrationRoute.IM,
            ProtocolId = i.ProtocolId, WoundWashed = i.WoundWashed, Rig = i.Rig, RigDose = Validation.Clean(i.RigDose), RigBatch = Validation.Clean(i.RigBatch),
            RigDate = i.Rig == RigType.None ? null : i.RigDate, Immunocompromised = i.Immunocompromised, AdvisedBy = doctor?.Name, Remarks = Validation.Clean(i.Remarks)
        };
        db.AntiRabiesPatients.Add(c);
        db.SaveChanges();
        c.CaseNo = $"ARV-{c.Id:D5}";
        db.SaveChanges();

        var course = ScheduleService.StartCourse(db, patientId, i.ProtocolId, i.FirstVaccineDate, c.Id);
        if (i.Immunocompromised || i.Category == BiteCategory.III)
        {
            course.NeedsClinicalReview = true;
            course.ReviewReason = (i.Immunocompromised ? "Immunocompromised patient. " : "") + (i.Category == BiteCategory.III ? "Category III exposure – professional assessment (RIG)." : "");
        }
        db.SaveChanges();
        c.CourseId = course.Id;
        Audit.Add(db, "Register", "AntiRabiesPatients", c.CaseNo, $"{patient.PatientNo} cat {i.Category} {protocol.Name}");
        db.SaveChanges();
        tx.Commit();
        return (c.Id, patientId);
    }

    public static List<RabiesCaseRow> Cases(VaccinatorDbContext db, int? patientId = null, RabiesList list = RabiesList.All, string? search = null)
    {
        var q = db.AntiRabiesPatients.AsNoTracking().Include(c => c.Patient).Include(c => c.Protocol).AsQueryable();
        if (patientId is int pid) q = q.Where(c => c.PatientId == pid);
        var cases = q.OrderByDescending(c => c.Id).Take(3000).ToList();
        var courseIds = cases.Where(c => c.CourseId != null).Select(c => c.CourseId!.Value).ToList();
        var appts = db.Appointments.AsNoTracking().Where(a => courseIds.Contains(a.CourseId)).ToList().GroupBy(a => a.CourseId).ToDictionary(g => g.Key, g => g.ToList());
        var courses = db.PatientCourses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionary(c => c.Id);
        var today = Fmt.Today;

        var rows = new List<RabiesCaseRow>();
        foreach (var c in cases)
        {
            var a = c.CourseId is int cid && appts.TryGetValue(cid, out var l) ? l : new List<Appointment>();
            var course = c.CourseId is int cid2 && courses.TryGetValue(cid2, out var co) ? co : null;
            var given = a.Count(x => x.Status == AppointmentStatus.Completed);
            var next = a.Where(x => x.Status == AppointmentStatus.Pending).OrderBy(x => x.DoseNumber).FirstOrDefault();
            string status;
            if (course?.Status == CourseStatus.Stopped) status = "Stopped";
            else if (next is null) status = "Completed";
            else if (next.DueDate < today) status = $"OVERDUE – {next.DoseLabel}";
            else if (next.DueDate == today) status = $"Due today – {next.DoseLabel}";
            else status = $"Dose {next.DoseNumber} pending";

            var include = list switch
            {
                RabiesList.FirstDosePending => next?.DoseNumber == 1,
                RabiesList.SecondDosePending => next?.DoseNumber == 2,
                RabiesList.ThirdDosePending => next?.DoseNumber == 3,
                RabiesList.FourthDosePending => next?.DoseNumber == 4,
                RabiesList.DueToday => next?.DueDate == today,
                RabiesList.Upcoming => next is not null && next.DueDate > today,
                RabiesList.Overdue => next is not null && next.DueDate < today && course?.Status == CourseStatus.Active,
                RabiesList.Completed => next is null && course?.Status != CourseStatus.Stopped,
                _ => true
            };
            if (!include) continue;
            var animal = c.AnimalType == AnimalType.Other && c.AnimalOther is not null ? c.AnimalOther : c.AnimalType.ToString();
            rows.Add(new RabiesCaseRow(c.Id, c.CaseNo, c.PatientId, c.Patient!.PatientNo, c.Patient.Name, c.Patient.Mobile, c.BiteDate, animal, $"Category {c.Category}",
                c.Protocol!.Name, given, a.Count, next?.DueDate, status, c.Category == BiteCategory.III || c.Immunocompromised || (course?.NeedsClinicalReview ?? false)));
        }

        var s = Validation.Clean(search)?.ToLower();
        if (s is not null) rows = rows.Where(r => r.PatientName.ToLower().Contains(s) || r.PatientNo.ToLower().Contains(s) || r.CaseNo.ToLower().Contains(s) || (r.Mobile ?? "").Contains(s)).ToList();
        return rows;
    }

    public static List<RabiesCaseRow> Cases(RabiesList list, string? search = null)
    {
        using var db = DbFactory.Create();
        return Cases(db, null, list, search);
    }

    public static AntiRabiesCase Load(int caseId)
    {
        using var db = DbFactory.Create();
        return db.AntiRabiesPatients.AsNoTracking().Include(c => c.Patient).Include(c => c.Protocol).First(c => c.Id == caseId);
    }

    public static int? NextAppointment(int caseId)
    {
        using var db = DbFactory.Create();
        var c = db.AntiRabiesPatients.Find(caseId);
        if (c?.CourseId is not int cid) return null;
        return db.Appointments.Where(a => a.CourseId == cid && a.Status == AppointmentStatus.Pending).OrderBy(a => a.DoseNumber).Select(a => (int?)a.Id).FirstOrDefault();
    }
}

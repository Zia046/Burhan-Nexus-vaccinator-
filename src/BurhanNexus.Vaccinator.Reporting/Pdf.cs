using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BurhanNexus.Vaccinator.Reporting;

/// <summary>All printed documents (A4, A5 for receipts). Every page carries the center header and the Burhan Nexus footer.</summary>
public static class Pdf
{
    private const string Teal = "#159C91";
    private const string Navy = "#123B50";
    private const string Light = "#E8F5F2";

    static Pdf()
    {
        // QuestPDF Community licence (free below USD 1M annual revenue). Re-check the terms before large-scale commercial sale.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static byte[] Build(CenterProfile center, string title, Action<ColumnDescriptor> body, bool a5 = false, bool landscape = false)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            var size = a5 ? PageSizes.A5 : PageSizes.A4;
            page.Size(landscape ? size.Landscape() : size);
            page.Margin(a5 ? 22 : 30);
            page.DefaultTextStyle(t => t.FontSize(a5 ? 9 : 10).FontColor("#17384B"));

            page.Header().Column(h =>
            {
                h.Item().Row(r =>
                {
                    if (center.Logo is { Length: > 0 })
                        r.ConstantItem(56).Height(56).Image(center.Logo).FitArea();
                    r.RelativeItem().PaddingLeft(center.Logo is { Length: > 0 } ? 10 : 0).Column(c =>
                    {
                        c.Item().Text(center.CenterName).FontSize(a5 ? 14 : 17).Bold().FontColor(Navy);
                        if (!string.IsNullOrWhiteSpace(center.Address)) c.Item().Text(center.Address).FontSize(9);
                        var contact = string.Join("   ", new[] { center.Phone is null ? null : "Phone: " + center.Phone, center.Email is null ? null : "Email: " + center.Email }.Where(x => x != null));
                        if (contact.Length > 0) c.Item().Text(contact).FontSize(9);
                    });
                    r.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("BURHAN NEXUS").FontSize(11).Bold().FontColor(Teal);
                        c.Item().AlignRight().Text("VACCINATOR").FontSize(9).Bold().FontColor(Navy);
                    });
                });
                h.Item().PaddingTop(6).Background(Navy).PaddingVertical(5).AlignCenter().Text(title.ToUpperInvariant()).FontSize(a5 ? 10 : 12).Bold().FontColor("#FFFFFF");
            });

            page.Content().PaddingVertical(10).Column(body);

            page.Footer().Column(f =>
            {
                f.Item().LineHorizontal(0.5f).LineColor("#C5D5DF");
                f.Item().PaddingTop(3).Row(r =>
                {
                    r.RelativeItem().Text($"SOFTWARE BY BURHAN NEXUS · {center.DeveloperMobile} · {center.DeveloperEmail}").FontSize(7).FontColor("#64748B");
                    r.ConstantItem(170).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7).FontColor("#64748B"));
                        t.Span($"Printed {Fmt.DT(DateTime.Now)} · Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });
        })).GeneratePdf();
    }

    // ---------------------------------------------------------------- building blocks

    private static void Info(ColumnDescriptor col, params (string Label, string? Value)[] items)
    {
        col.Item().Border(0.5f).BorderColor("#C5D5DF").Padding(8).Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(105); c.RelativeColumn(); c.ConstantColumn(105); c.RelativeColumn(); });
            foreach (var (label, value) in items)
            {
                t.Cell().PaddingVertical(2).Text(label).SemiBold().FontColor("#475569");
                t.Cell().PaddingVertical(2).Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
            }
        });
    }

    private static void Grid(ColumnDescriptor col, string[] headers, float[] widths, IEnumerable<string?[]> rows, Func<int, string?>? rowColor = null)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var w in widths) { if (w <= 0) c.RelativeColumn(); else c.ConstantColumn(w); } });
            t.Header(h =>
            {
                foreach (var head in headers)
                    h.Cell().Background(Light).BorderBottom(1).BorderColor(Teal).Padding(4).Text(head).Bold().FontSize(8.5f).FontColor(Navy);
            });
            var i = 0;
            foreach (var row in rows)
            {
                var bg = rowColor?.Invoke(i) ?? (i % 2 == 0 ? "#FFFFFF" : "#F7FBFB");
                foreach (var cell in row)
                    t.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4).Text(cell ?? "").FontSize(8.5f);
                i++;
            }
        });
    }

    private static void Heading(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(10).PaddingBottom(4).Text(text).FontSize(11).Bold().FontColor(Teal);

    private static void Signature(ColumnDescriptor col, string? next = null)
    {
        col.Item().PaddingTop(24).Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                if (next is not null) c.Item().Text("NEXT APPOINTMENT: " + next).Bold().FontSize(11).FontColor(Navy);
            });
            r.ConstantItem(200).Column(c =>
            {
                c.Item().PaddingTop(20).LineHorizontal(0.8f);
                c.Item().AlignCenter().Text("Healthcare worker signature & stamp").FontSize(8);
            });
        });
    }

    private static (string, string?)[] PatientInfo(PatientProfile p)
    {
        var x = p.Patient;
        return new (string, string?)[]
        {
            ("Patient ID", x.PatientNo), ("Name", x.Name),
            ("Father / Husband", x.GuardianName), ("Gender", x.Gender.ToString()),
            ("Date of birth", Fmt.D(x.DateOfBirth)), ("Age", p.AgeText),
            ("CNIC", x.Cnic), ("Mobile", string.Join(" / ", new[] { x.Mobile, x.AlternativeMobile }.Where(m => m != null))),
            ("Advised by", p.DoctorName), ("Category", Fmt.Category(x.Category)),
            ("Registered", Fmt.D(x.RegistrationDate)), ("Address", x.Address)
        };
    }

    // ---------------------------------------------------------------- documents

    public static byte[] RegistrationForm(CenterProfile center, PatientProfile p) => Build(center, "Patient Registration Form", col =>
    {
        Info(col, PatientInfo(p));
        if (p.Patient.Newborn is { } n)
        {
            Heading(col, "Birth details");
            Info(col, ("Mother", n.MotherName), ("Mother mobile", n.MotherMobile), ("Time of birth", n.TimeOfBirth is TimeOnly t ? Fmt.T(t) : null),
                ("Birth weight", n.BirthWeightKg is decimal w ? $"{w:0.##} kg" : null), ("Birth place", n.BirthPlace), ("Father", n.FatherName));
        }
        if (!string.IsNullOrWhiteSpace(p.Patient.Remarks)) { Heading(col, "Remarks"); col.Item().Text(p.Patient.Remarks); }
        if (p.Patient.IsImmunocompromised) col.Item().PaddingTop(8).Text("FLAG: Immunocompromised – clinical review required for vaccination decisions.").Bold().FontColor("#B91C1C");
        Signature(col);
    });

    public static byte[] VaccinationCard(CenterProfile center, PatientProfile p) => Build(center, "Vaccination Card", col =>
    {
        Info(col, PatientInfo(p));
        Heading(col, "Vaccination schedule");
        Grid(col, new[] { "Vaccine", "Dose", "Due date", "Given on", "Batch", "Status" }, new float[] { 0, 90, 70, 70, 75, 65 },
            p.Doses.Where(d => d.Status != "Cancelled").OrderBy(d => d.DueDate).Select(d => new[] { d.Vaccine, d.DoseLabel, d.DueText, d.GivenText, d.BatchNumber, d.Status }));
        var extra = p.History.Where(h => !p.Doses.Any(d => d.GivenOn == h.Date && d.Vaccine == h.Vaccine)).ToList();
        if (extra.Count > 0)
        {
            Heading(col, "Other vaccinations");
            Grid(col, new[] { "Date", "Vaccine", "Dose", "Route", "Batch", "By" }, new float[] { 70, 0, 80, 40, 75, 90 },
                extra.Select(h => new[] { h.DateText, h.Vaccine, h.Dose, h.Route, h.Batch, h.AdministeredBy }));
        }
        Signature(col, p.NextAppointment is { } n ? $"{n.DueText} – {n.Title}" : null);
    });

    public static byte[] NewbornCard(CenterProfile center, PatientProfile p) => Build(center, "Newborn Immunization Card (EPI)", col =>
    {
        var x = p.Patient;
        var n = x.Newborn;
        Info(col, ("Patient ID", x.PatientNo), ("Baby name", x.Name), ("Date of birth", Fmt.D(x.DateOfBirth)), ("Age", p.AgeText), ("Gender", x.Gender.ToString()),
            ("Father", n?.FatherName ?? x.GuardianName), ("Mother", n?.MotherName), ("Mother mobile", n?.MotherMobile ?? x.Mobile),
            ("Birth weight", n?.BirthWeightKg is decimal w ? $"{w:0.##} kg" : null), ("Birth place", n?.BirthPlace), ("Address", x.Address), ("Registered", Fmt.D(x.RegistrationDate)));

        Heading(col, "Immunization timeline");
        var dob = x.DateOfBirth ?? x.RegistrationDate;
        foreach (var group in p.Doses.Where(d => d.Status != "Cancelled").GroupBy(d => d.CalculatedDate).OrderBy(g => g.Key))
        {
            var age = group.Key == dob ? "At birth" : DateMath.Age(dob, group.Key).Text;
            col.Item().PaddingTop(6).Background(Light).Padding(4).Text($"{age}  ·  due {Fmt.D(group.Min(d => d.DueDate))}").Bold().FontColor(Navy);
            Grid(col, new[] { "Vaccine", "Dose", "Due", "Given on", "Batch", "Status" }, new float[] { 0, 80, 70, 70, 75, 65 },
                group.OrderBy(d => d.Vaccine).Select(d => new[] { d.Vaccine, d.DoseLabel, d.DueText, d.GivenText, d.BatchNumber, d.Status }));
        }
        col.Item().PaddingTop(8).Text("Schedule source: Federal Directorate of Immunization (FDI) Pakistan – routine EPI schedule. Bring this card to every visit.").FontSize(8).Italic();
        Signature(col, p.NextAppointment is { } next ? $"{next.DueText} – {next.Title}" : null);
    });

    public static byte[] AntiRabiesCard(CenterProfile center, PatientProfile p, AntiRabiesCase c) => Build(center, "Anti-Rabies Vaccination Card", col =>
    {
        Info(col, PatientInfo(p));
        Heading(col, "Exposure details");
        Info(col, ("Case No", c.CaseNo), ("Bite date", Fmt.D(c.BiteDate)),
            ("Animal", c.AnimalType == AnimalType.Other ? c.AnimalOther ?? "Other" : c.AnimalType.ToString()), ("Category", $"Category {c.Category}"),
            ("Bite location", c.BiteLocation), ("Wound washed", c.WoundWashed ? "Yes" : "No"),
            ("Previously vaccinated", c.PreviouslyVaccinated ? "Yes – " + c.PreviousVaccinationDetails : "No"), ("Vaccine brand", c.VaccineBrand),
            ("Protocol", c.Protocol?.Name), ("Route", c.Route.ToString()),
            ("RIG", c.Rig == RigType.None ? "Not recorded" : $"{c.Rig} {c.RigDose} {Fmt.D(c.RigDate)} batch {c.RigBatch}"), ("Advised by", c.AdvisedBy));
        if (c.Category == BiteCategory.III || c.Immunocompromised)
            col.Item().PaddingTop(6).Background("#FDECEC").Padding(6).Text(
                (c.Category == BiteCategory.III ? "CATEGORY III EXPOSURE – professional assessment required. " : "") +
                (c.Immunocompromised ? "IMMUNOCOMPROMISED – clinical review required." : "")).Bold().FontColor("#991B1B");

        Heading(col, "Dose schedule");
        Grid(col, new[] { "Dose", "Due date", "Given on", "Batch", "Status" }, new float[] { 0, 80, 80, 90, 80 },
            p.Doses.Where(d => d.CourseId == c.CourseId).OrderBy(d => d.DoseNumber).Select(d => new[] { d.DoseLabel, d.DueText, d.GivenText, d.BatchNumber, d.Status }));
        col.Item().PaddingTop(8).Text("Come on the due dates. If you miss a date, come as soon as possible – the course continues and is not restarted.").FontSize(9);
        col.Item().Text("Reference: " + (c.Protocol?.Reference ?? "")).FontSize(7.5f).Italic();
        var next = p.Doses.Where(d => d.CourseId == c.CourseId && d.Status != "Completed" && d.Status != "Cancelled").OrderBy(d => d.DueDate).FirstOrDefault();
        Signature(col, next is null ? null : $"{next.DueText} – {next.DoseLabel}");
    });

    public static byte[] UpcomingSchedule(CenterProfile center, PatientProfile p) => Build(center, "Upcoming Dose Schedule", col =>
    {
        Info(col, ("Patient ID", p.Patient.PatientNo), ("Name", p.Patient.Name), ("Father / Husband", p.Patient.GuardianName), ("Mobile", p.Patient.Mobile));
        Heading(col, "Pending doses");
        var rows = p.Doses.Where(d => d.Status is "Pending" or "Due today" or "Overdue").OrderBy(d => d.DueDate).ToList();
        if (rows.Count == 0) col.Item().Text("No pending doses.");
        else Grid(col, new[] { "Due date", "Day", "Vaccine", "Dose", "Status" }, new float[] { 80, 70, 0, 100, 80 },
            rows.Select(d => new[] { d.DueText, d.DueDate.DayOfWeek.ToString(), d.Vaccine, d.DoseLabel, d.Status }));
        Signature(col);
    });

    public static byte[] PatientHistory(CenterProfile center, PatientProfile p) => Build(center, "Patient Vaccination History", col =>
    {
        Info(col, PatientInfo(p));
        Heading(col, "Administered doses");
        if (p.History.Count == 0) col.Item().Text("No vaccination recorded.");
        else Grid(col, new[] { "Date", "Vaccine", "Dose", "Route / site", "Batch", "By" }, new float[] { 62, 0, 75, 85, 70, 80 },
            p.History.OrderBy(h => h.Date).Select(h => new[] { h.DateText, h.Vaccine, h.Dose, $"{h.Route} {h.Site}", h.Batch, h.AdministeredBy + (h.Historical ? " (old card)" : "") }));
        if (p.Missed.Count > 0)
        {
            Heading(col, "Missed / overdue doses");
            Grid(col, new[] { "Due date", "Vaccine", "Dose" }, new float[] { 80, 0, 120 }, p.Missed.Select(d => new[] { d.DueText, d.Vaccine, d.DoseLabel }));
        }
        if (p.RabiesCases.Count > 0)
        {
            Heading(col, "Anti-Rabies cases");
            Grid(col, new[] { "Case", "Bite date", "Animal", "Category", "Doses", "Status" }, new float[] { 60, 65, 60, 65, 45, 0 },
                p.RabiesCases.Select(r => new[] { r.CaseNo, r.BiteText, r.Animal, r.Category, r.Progress, r.Status }));
        }
        if (p.Insulin.Count > 0)
        {
            Heading(col, "Insulin issued");
            Grid(col, new[] { "Date", "Insulin", "Batch", "Vials", "Doctor", "Amount" }, new float[] { 62, 0, 65, 35, 85, 60 },
                p.Insulin.Select(i => new[] { i.DateText, i.Product, i.Batch, i.Vials.ToString(), i.Doctor, i.TotalText }));
        }
        Signature(col);
    });

    public static byte[] SchedulePreview(CenterProfile center, string vaccine, string protocol, string? patient, IEnumerable<PreviewDose> doses, string? reference) =>
        Build(center, "Vaccine Schedule", col =>
        {
            Info(col, ("Vaccine", vaccine), ("Protocol", protocol), ("Patient", patient), ("Printed by", Session.DisplayName));
            Heading(col, "Dose dates");
            Grid(col, new[] { "Dose", "Schedule", "Date", "Day", "Volume", "Route", "Site" }, new float[] { 0, 95, 65, 60, 65, 75, 80 },
                doses.Select(d => new[] { d.Label, d.Schedule, d.DateText, d.Weekday, d.Volume, d.Route, d.Site }));
            if (reference is not null) col.Item().PaddingTop(8).Text("Reference: " + reference).FontSize(8).Italic();
            col.Item().PaddingTop(4).Text("Dates follow the selected protocol. A healthcare professional confirms the protocol for each patient.").FontSize(8).Italic();
            Signature(col);
        });

    public static byte[] AppointmentList(CenterProfile center, string title, IEnumerable<AppointmentRow> rows) => Build(center, title, col =>
    {
        var list = rows.ToList();
        col.Item().Text($"{list.Count} appointment(s)").SemiBold();
        Grid(col, new[] { "Due", "Patient ID", "Patient", "Mobile", "Vaccine", "Dose", "Previous", "Note" }, new float[] { 58, 62, 0, 70, 70, 70, 58, 70 },
            list.Select(a => new[] { a.DueText, a.PatientNo, a.PatientName, a.Mobile, a.Vaccine, a.DoseLabel, a.PreviousText, a.Status == "Overdue" ? $"{a.DaysOverdue} d late" : a.Note }));
    }, landscape: true);

    public static byte[] PeriodReport(CenterProfile center, PeriodReport r) => Build(center, r.Title, col =>
    {
        Info(col, ("From", Fmt.D(r.From)), ("To", Fmt.D(r.To)), ("Vaccinations", r.Vaccinations.Count.ToString()), ("New patients", r.NewPatients.ToString()),
            ("New anti-rabies cases", r.NewRabiesCases.ToString()), ("Doses wasted", r.Wastage.ToString()),
            ("Insulin vials issued", r.Insulin.Sum(i => i.Vials).ToString()), ("Insulin amount", Fmt.Money(r.Insulin.Sum(i => i.Total))));
        Heading(col, "Vaccine-wise summary");
        Grid(col, new[] { "Vaccine", "Doses given" }, new float[] { 0, 90 }, r.ByVaccine.Select(v => new[] { v.Label, v.Value.ToString() }));
        Heading(col, "Vaccination entries");
        Grid(col, new[] { "Date", "Time", "Patient ID", "Patient", "Vaccine", "Dose", "Batch", "By" }, new float[] { 55, 45, 60, 0, 65, 60, 60, 70 },
            r.Vaccinations.Select(v => new[] { v.DateText, v.TimeText, v.PatientNo, v.PatientName, v.Vaccine, v.Dose, v.Batch, v.AdministeredBy }));
        if (r.Insulin.Count > 0)
        {
            Heading(col, "Insulin dispensing");
            Grid(col, new[] { "Date", "Receipt", "Patient", "Insulin", "Vials", "Amount" }, new float[] { 55, 65, 0, 120, 35, 60 },
                r.Insulin.Select(i => new[] { i.DateText, i.TransactionNo, i.PatientName, i.Product, i.Vials.ToString(), i.TotalText }));
        }
    });

    public static byte[] StockReport(CenterProfile center, IEnumerable<BatchRow> batches) => Build(center, "Vaccine Stock Report", col =>
    {
        var list = batches.ToList();
        col.Item().Text($"Report date {Fmt.D(Fmt.Today)} · {list.Count} batch(es) · usable doses {list.Where(b => b.Status is "OK" or "LOW STOCK" or "EXPIRING SOON").Sum(b => b.Available)}").SemiBold();
        Grid(col, new[] { "Vaccine", "Brand", "Batch", "Expiry", "Received", "Available", "VVM", "Location", "Status" },
            new float[] { 0, 70, 70, 58, 48, 50, 45, 65, 70 },
            list.Select(b => new[] { b.Vaccine, b.Brand, b.BatchNumber, b.ExpiryText, b.Received.ToString(), b.Available.ToString(), b.Vvm.ToString(), b.Location, b.Status }));
    }, landscape: true);

    public static byte[] InsulinStockReport(CenterProfile center, IEnumerable<InsulinBatchRow> batches) => Build(center, "Insulin Stock Report", col =>
    {
        Grid(col, new[] { "Insulin", "Batch", "Expiry", "Received", "Available", "Location", "Status" }, new float[] { 0, 70, 60, 50, 55, 70, 70 },
            batches.Select(b => new[] { b.Product, b.BatchNumber, b.ExpiryText, b.Received.ToString(), b.Available.ToString(), b.Location, b.Status }));
    });

    public static byte[] InsulinReceipt(CenterProfile center, InsulinReceipt r) => Build(center, "Insulin Issue Receipt", col =>
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Text($"Receipt No: {r.TransactionNo}").Bold();
            row.RelativeItem().AlignRight().Text(Fmt.DT(r.At));
        });
        col.Item().PaddingTop(6).Text($"Patient: {r.PatientName} ({r.PatientNo})").Bold();
        if (r.GuardianName is not null) col.Item().Text($"Father / Husband: {r.GuardianName}");
        if (r.Mobile is not null || r.Cnic is not null) col.Item().Text($"Mobile: {r.Mobile}   CNIC: {r.Cnic}");
        col.Item().PaddingTop(8).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(40); c.ConstantColumn(60); c.ConstantColumn(65); });
            t.Header(h =>
            {
                h.Cell().BorderBottom(1).Text("Insulin").Bold(); h.Cell().BorderBottom(1).AlignRight().Text("Vials").Bold();
                h.Cell().BorderBottom(1).AlignRight().Text("Price").Bold(); h.Cell().BorderBottom(1).AlignRight().Text("Amount").Bold();
            });
            t.Cell().PaddingVertical(4).Text($"{r.Product} {r.InsulinType} {r.Strength} ({r.VialSize})\nBatch {r.Batch} · Exp {Fmt.D(r.Expiry)}");
            t.Cell().PaddingVertical(4).AlignRight().Text(r.Vials.ToString());
            t.Cell().PaddingVertical(4).AlignRight().Text(Fmt.Money(r.UnitPrice));
            t.Cell().PaddingVertical(4).AlignRight().Text(Fmt.Money(r.Total));
        });
        col.Item().AlignRight().PaddingTop(4).Text($"TOTAL: {Fmt.Money(r.Total)}").FontSize(12).Bold();
        if (r.Doctor is not null) col.Item().PaddingTop(6).Text($"Prescribed by: {r.Doctor}");
        if (r.Dosage is not null) col.Item().Text($"Prescribed dosage (as written by prescriber): {r.Dosage}");
        if (r.Remarks is not null) col.Item().Text($"Remarks: {r.Remarks}");
        col.Item().PaddingTop(6).Text("Store insulin in a refrigerator at +2 °C to +8 °C. Do not freeze. Follow your doctor's dosage instructions.").FontSize(8).Italic();
        col.Item().Text($"Issued by: {r.IssuedBy}").FontSize(8);
        Signature(col);
    }, a5: true);
}

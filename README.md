# Burhan Nexus Vaccinator

**Professional Vaccination, Immunization, Anti-Rabies & Insulin Management System**
Developed & Powered by **Burhan Nexus** · 03475093446 · ziaullah.046@gmail.com

A Windows desktop application for hospitals, vaccination centers and clinics in Pakistan. It works fully offline: all data is stored in a local SQLite database, with automatic daily backups.

## Download

Every push to `main` is built, tested and packaged by GitHub Actions. Open **Releases** and download:

- `BurhanNexus-Vaccinator-Setup.exe`: the installer (recommended)
- `BurhanNexus-Vaccinator-win-x64.zip`: the portable version

Requires Windows 10 or 11 (64-bit). No separate .NET install is needed.

On first start, enter the center name and create the administrator account.

## Modules (Home screen)

| # | Module | What it does |
|---|--------|--------------|
| 1 | New Patient Entry | Auto patient ID (BNV-000001), CNIC/mobile validation, duplicate check, doctor auto-suggest (new doctors are saved automatically) |
| 2 | Old Patient Search | Search by name, ID, CNIC, mobile, father/husband, doctor. Opens a profile with tabs for schedule, history, missed doses, anti-rabies, insulin, personal info and printed documents |
| 3 | Newborn Baby Registration | Age in days/weeks/months. Builds the full Pakistan EPI schedule from the date of birth (birth, 6/10/14 weeks, 9 and 15 months) |
| 4 | Check Vaccine Schedule | Calculates the dose dates for any protocol and start date, and prints them |
| 5 | Available Vaccines | Vaccine database with protocols, references and storage requirements |
| 6 | Anti-Rabies Management | Bite category, animal, wound washing, RIG, IM/ID/previously vaccinated/PrEP protocols, and lists of 1st/2nd/3rd/4th dose pending, due today, upcoming, overdue and completed patients |
| 7 | Insulin Management | Separate from vaccines: products, batches, issue, stock deduction, low-stock and expiry alerts, receipts, daily/monthly reports. **Never calculates insulin doses** |
| 8 | Today's Vaccinations | Doses due today and doses given today |
| 9 | Upcoming Vaccinations | Today, tomorrow, 7 days, 30 days; mark completed, reschedule, patient not arrived; print and Excel export |
| 10 | Missed / Overdue Doses | Overdue list with days late |
| 11 | Vaccination History | All administered doses with filters, print and Excel export |
| 12 | Doctor Directory | Add, edit, search; delete is administrator-only |
| 13 | Vaccine Inventory | Stock in/out, wastage, expired disposal, quarantine, VVM, batch tracking, cold-chain temperature log with excursion warnings |
| 14 | Reports & Statistics | Daily, monthly and any-period reports, stock reports, Excel export, audit trail |
| 15 | Print Vaccination Card | Vaccination card, newborn card, anti-rabies card, schedule, history, registration form |
| 16 | Settings | Center name and logo, date/time format, printer, users and roles, vaccine database and protocol editor, backup/restore, about |

## Scheduling engine

- The first administered dose (or the date of birth for EPI) is the anchor.
- Days, weeks and **calendar months/years** are supported. Months are real calendar months (31 Jan + 1 month = 28/29 Feb), never 30 days.
- Every dose is saved as its own appointment.
- A late dose pushes the following doses forward while keeping the protocol's minimum interval. The course is **never restarted**, and dates of doses already given are never changed.
- Doses that are much later than planned flag the course for **clinical review**.
- Entering an old dose from a card recalculates only the remaining doses.

## Medical safety

- The software records and schedules. It does not diagnose, decide exposure categories or prescribe.
- Each protocol carries its reference (FDI Pakistan EPI schedule, WHO position papers). **No protocol starts out approved.** The center's medical officer approves each one in *Settings → Vaccine Database & Protocols*, and any schedule change withdraws the approval.
- Volumes that differ by brand are marked "as per product leaflet".
- Expired, quarantined and VVM stage 3/4 batches cannot be used. Every administered dose is linked to its batch.
- Category III exposures, missing RIG and immunocompromised patients raise warnings.

## Roles

| Role | Access |
|------|--------|
| Administrator | Everything |
| Vaccinator | Patients, vaccinations, anti-rabies, insulin issue, reminders, reports |
| Receptionist | Registration, search, reminders (reschedule / not arrived), printing |
| Inventory Manager | Vaccine and insulin stock, reports |

## Data location

`C:\ProgramData\BurhanNexus\Vaccinator\`: database (`vaccinator.db`), `backups\` (one automatic backup per day, newest 30 kept) and `logs\`.

## Build from source

Install the .NET 10 SDK and run `build-and-test.cmd`, or:

```
dotnet build BurhanNexus.Vaccinator.sln -c Release
dotnet test tests/BurhanNexus.Vaccinator.Tests
```

Projects: `Domain` (entities, scheduling engine, vaccine catalogue) → `Infrastructure` (EF Core SQLite, backup) → `Application` (services, rules) → `Reporting` (QuestPDF, ClosedXML) → `Desktop` (WPF, MVVM).

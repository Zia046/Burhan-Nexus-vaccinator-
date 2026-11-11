using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BurhanNexus.Vaccinator.Application;
using BurhanNexus.Vaccinator.Desktop;
using BurhanNexus.Vaccinator.Desktop.ViewModels;
using BurhanNexus.Vaccinator.Infrastructure;

namespace BurhanNexus.Vaccinator.UiTests;

/// <summary>
/// Loads the real application resources, builds every screen with its real view-model on a test database,
/// applies all templates and lays each screen out at 1100×700, 1366×768 and 1920×1080. A missing resource,
/// broken template or binding-time crash fails here instead of on the customer's computer.
/// Pictures of the screens are saved for visual review (artifact "ui-screens").
/// </summary>
public class ScreenTests
{
    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException("UI test failed: " + failure);
    }

    private static void Force(DependencyObject root)
    {
        if (root is FrameworkElement fe) fe.ApplyTemplate();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Force(VisualTreeHelper.GetChild(root, i));
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            e.Measure(new Size(w, h));
            e.Arrange(new Rect(0, 0, w, h));
            e.UpdateLayout();
            Force(e);
        }
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static void Render(FrameworkElement e, string name, int w, int h)
    {
        var dir = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    [Fact]
    public void Every_screen_builds_with_data_and_lays_out()
    {
        RunSta(() =>
        {
            AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "bnv-ui-" + Guid.NewGuid().ToString("N")));
            DbFactory.Initialize();
            UserService.FirstSetup("Demo Vaccination Center, Buner", "Administrator", "admin", "secret1");
            UserService.Login("admin", "secret1");
            SeedDemo();

            try { System.Windows.Application.ResourceAssembly = typeof(App).Assembly; } catch (InvalidOperationException) { }
            var app = new App();
            app.InitializeComponent();

            var main = new MainViewModel();
            var window = new MainWindow { DataContext = main };
            var host = (FrameworkElement)window.Content;

            // Setup and login screens
            main.Start();
            Layout(host, 1366, 768);
            Render(host, "00-Login", 1366, 768);

            main.ShowShell();
            var shell = (ShellViewModel)main.Current!;
            var patientId = PatientService.Search("Ahmad").First().Id;
            var newbornId = PatientService.Search("Baby").First().Id;

            var pages = new List<(string, Func<PageViewModel>)>
            {
                ("01-Home", () => new HomeViewModel(shell)),
                ("02-Dashboard", () => new DashboardViewModel(shell)),
                ("03-NewPatient", () => new PatientEntryViewModel(shell, null)),
                ("04-Search", () => new PatientSearchViewModel(shell)),
                ("05-Profile", () => new PatientProfileViewModel(shell, patientId)),
                ("06-NewbornProfile", () => new PatientProfileViewModel(shell, newbornId)),
                ("07-Newborn", () => new NewbornViewModel(shell)),
                ("08-ScheduleCheck", () => new ScheduleCheckViewModel(shell)),
                ("09-Vaccines", () => new VaccinesViewModel(shell)),
                ("10-AntiRabies", () => new RabiesViewModel(shell)),
                ("11-Insulin", () => new InsulinViewModel(shell)),
                ("12-Today", () => new TodayViewModel(shell)),
                ("13-Upcoming", () => new AppointmentsViewModel(shell, AppointmentFilter.Next7Days)),
                ("14-Overdue", () => new AppointmentsViewModel(shell, AppointmentFilter.Overdue)),
                ("15-History", () => new HistoryViewModel(shell)),
                ("16-Doctors", () => new DoctorsViewModel(shell)),
                ("17-Inventory", () => new InventoryViewModel(shell)),
                ("18-Reports", () => new ReportsViewModel(shell)),
                ("19-PrintCard", () => new PrintCardViewModel(shell)),
                ("20-Settings", () => new SettingsViewModel(shell)),
                ("21-VaccineDatabase", () => new SettingsViewModel(shell, 2)),
                ("22-About", () => new SettingsViewModel(shell, 5)),
                ("23-Administer", () => new AdministerViewModel(shell, VaccinationService.DefaultsFor(patientId, VaccinationService.Vaccines().First(v => v.Name == "Hepatitis B").Id,
                    ScheduleService.Protocols().First(p => p.VaccineName == "Hepatitis B" && p.Name.StartsWith("Hep B 0-1-6")).Id))),
                ("24-ProtocolEditor", () => new ProtocolEditorViewModel(shell, ScheduleService.Protocols().First(p => p.VaccineName == "Rabies").Id, ScheduleService.Protocols().First(p => p.VaccineName == "Rabies").VaccineId))
            };

            foreach (var (name, make) in pages)
            {
                shell.Navigate(make());
                foreach (var (w, h) in new[] { (1100, 700), (1366, 768), (1920, 1080) })
                {
                    Layout(host, w, h);
                    if (w == 1366) Render(host, name, w, h);
                }
            }

            // Every view type must also build on its own (no data context)
            var views = typeof(App).Assembly.GetTypes()
                .Where(t => typeof(UserControl).IsAssignableFrom(t) && t.Namespace == "BurhanNexus.Vaccinator.Desktop.Views" && t.GetConstructor(Type.EmptyTypes) != null).ToList();
            Assert.True(views.Count >= 20, "views found: " + views.Count);
            foreach (var t in views) Layout((FrameworkElement)Activator.CreateInstance(t)!, 1100, 700);
        });
    }

    private static void SeedDemo()
    {
        var hepB = VaccinationService.Vaccines().First(v => v.Name == "Hepatitis B").Id;
        var rabiesV = VaccinationService.Vaccines().First(v => v.Name == "Rabies").Id;
        InventoryService.StockIn(new BatchInput { VaccineId = hepB, BatchNumber = "HB2401", Brand = "Demo", Quantity = 50, ExpiryDate = Fmt.Today.AddMonths(8) });
        InventoryService.StockIn(new BatchInput { VaccineId = rabiesV, BatchNumber = "RB77", Brand = "Demo", Quantity = 6, ExpiryDate = Fmt.Today.AddDays(20) });
        InventoryService.AddTemperature("Main refrigerator", 4.5m, null);
        InventoryService.AddTemperature("Main refrigerator", 9.2m, "Power cut");

        var pid = PatientService.Register(new PatientInput { Name = "Ahmad Khan", GuardianName = "Karim Khan", AgeYears = 32, Mobile = "03001234567", AdvisedBy = "Dr. Salma" });
        var hepProtocol = ScheduleService.Protocols().First(p => p.VaccineName == "Hepatitis B" && p.Name.StartsWith("Hep B 0-1-6")).Id;
        var batch = InventoryService.UsableBatches(hepB, Fmt.Today).First().Id;
        VaccinationService.Administer(new AdministrationInput { PatientId = pid, VaccineId = hepB, ProtocolId = hepProtocol, DoseNumber = 1, BatchId = batch, AdministeredBy = "Nurse Amina" });

        PatientService.RegisterNewborn(new NewbornInput { BabyName = "Baby Fatima", FatherName = "Imran", MotherName = "Ayesha", MotherMobile = "03111234567", Gender = Domain.Gender.Female, DateOfBirth = Fmt.Today.AddDays(-45) });

        var rabiesProtocol = ScheduleService.Protocols().First(p => p.VaccineName == "Rabies").Id;
        RabiesService.Register(new RabiesInput { NewPatient = new PatientInput { Name = "Zahid", AgeYears = 18 }, ProtocolId = rabiesProtocol, Category = Domain.BiteCategory.III, WoundWashed = true });

        InsulinService.SaveProduct(null, "Demo Insulin", "Regular", "100 IU/mL", "10 mL", 1100m, 5, null);
        var product = InsulinService.Products().First();
        InsulinService.StockIn(product.Id, "IN-55", null, Fmt.Today.AddMonths(6), 12, "Fridge 2", 5m);
        InsulinService.Issue(new InsulinIssueInput { PatientId = pid, BatchId = InsulinService.Batches(product.Id, true).First().Id, Vials = 2, UnitPrice = 1100m, PrescribingDoctor = "Dr. Hamid", PrescribedDosage = "As per prescription" });
    }
}

using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;
    private AppDatabase? database;
    private SecuritySession? session;
    public static string DataDirectory { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int captureIndex = Array.FindIndex(e.Args, value => string.Equals(value, "--capture-ui", StringComparison.OrdinalIgnoreCase));
        if (captureIndex >= 0)
        {
            string output = captureIndex + 1 < e.Args.Length ? e.Args[captureIndex + 1] : Path.Combine(Environment.CurrentDirectory, "Saudi-Patient-Records-v6.0.0-Actual-UI.png");
            int width = captureIndex + 2 < e.Args.Length && int.TryParse(e.Args[captureIndex + 2], out int parsedWidth) ? parsedWidth : 1440;
            int height = captureIndex + 3 < e.Args.Length && int.TryParse(e.Args[captureIndex + 3], out int parsedHeight) ? parsedHeight : 900;
            Environment.ExitCode = CaptureActualInterface(output, width, height);
            Shutdown(Environment.ExitCode);
            return;
        }
        if (e.Args.Any(value => string.Equals(value, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = RunStandaloneSelfTest();
            Shutdown(Environment.ExitCode);
            return;
        }

        RemoveLegacyAutoStartEntries();

        instanceMutex = new Mutex(true, @"Local\SaudiPatientRecordsV6_938F616E_7287_4C2E_86B2_5D90B6F29A0C", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("البرنامج يعمل بالفعل. افتحه من شريط المهام.", "سجلات المراجعين", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnexpectedError(args.ExceptionObject as Exception, "Fatal");
        DispatcherUnhandledException += (_, args) =>
        {
            LogUnexpectedError(args.Exception, "UI");
            MessageBox.Show("حدث خطأ غير متوقع. البيانات المكتملة محفوظة؛ أعد المحاولة أو أعد تشغيل البرنامج إذا تكرر الخطأ.", "خطأ غير متوقع", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            args.Handled = true;
        };

        try
        {
            DataDirectory = AppProfile.ResolveDataDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AppProfile.Initialize(DataDirectory);
            AppDatabase.CleanupTemporaryAttachments();

            var security = new AppSecurity(DataDirectory);
            security.EnsureDefaultConfiguration();
            if (security.IsLoginRequired)
            {
                var login = new LoginWindow(security);
                if (login.ShowDialog() != true || login.Session is null) { Shutdown(); return; }
                session = login.Session;
            }
            else
            {
                session = security.OpenWithoutLogin();
            }

            database = new AppDatabase(DataDirectory, session.MaterializeDatabasePassword(), session.DisplayName, session.Role);
            security.FlushPendingAudit(database);
            var main = new MainWindow(database, new BackupService(DataDirectory), security, session);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            LogUnexpectedError(ex, "Startup");
            MessageBox.Show("تعذر فتح قاعدة البيانات.\n\n" + ex.Message, "تعذر التشغيل", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { database?.Checkpoint(); } catch { }
        database?.Dispose();
        session?.Dispose();
        try { AppDatabase.CleanupTemporaryAttachments(); } catch { }
        instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static int RunStandaloneSelfTest()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SaudiPatientRecordsWpf_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var security = new AppSecurity(folder);
            if (!security.EnsureDefaultConfiguration() || security.IsLoginRequired) return 3;
            using SecuritySession local = security.OpenWithoutLogin();
            using var db = new AppDatabase(folder, local.MaterializeDatabasePassword(), local.DisplayName, local.Role);
            if (db.CountActivePatients() != 0 || db.GetSettings().NextFileNumber != 1) return 2;
            VerifyUiComposition(db, security, local, folder);
            security.AddUser(local, "tester1", "مدير الفحص", "مدير", "test1234");
            security.SetLoginRequired(local, true);
            using SecuritySession authenticated = security.Login("tester1", "test1234");
            if (!authenticated.IsAdmin) return 4;
            security.SetLoginRequired(local, false);
            db.Checkpoint();
            return 0;
        }
        catch { return 1; }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    private static void VerifyUiComposition(AppDatabase database, AppSecurity security, SecuritySession session, string folder)
    {
        System.Windows.Window[] windows =
        {
            new MainWindow(database, new BackupService(folder), security, session),
            new PatientEditorWindow(database, null, false),
            new AppointmentEditorWindow(database, null, null),
            new TaskEditorWindow(database, null, null),
            new AccountManagerWindow(security, session),
            new ClosureDatesWindow(database),
            new RecycleBinWindow(database),
            new ReminderWindow(Array.Empty<PatientRecordsSaudi.Models.Appointment>(), Array.Empty<PatientRecordsSaudi.Models.PatientTask>(), _ => { }),
            new UserEditorWindow(),
            new PasswordWindow("فحص الواجهة")
        };
        foreach (System.Windows.Window window in windows) window.Close();
    }

    private static void LogUnexpectedError(Exception? exception, string area)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), DateTime.UtcNow.ToString("O") + " | " + area + " | " + (exception?.GetType().FullName ?? "Unknown") + Environment.NewLine);
        }
        catch { }
    }

    private static int CaptureActualInterface(string outputPath, int requestedWidth, int requestedHeight)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SaudiPatientRecordsCaptureV6_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var security = new AppSecurity(folder);
            security.EnsureDefaultConfiguration();
            using SecuritySession local = security.OpenWithoutLogin();
            using var db = new AppDatabase(folder, local.MaterializeDatabasePassword(), local.DisplayName, local.Role);
            AppSettings settings = db.GetSettings();
            settings.ClinicName = "مركز الرعاية الصحية";
            settings.ClinicPhone = "011 000 0000";
            settings.ClinicAddress = "المملكة العربية السعودية";
            db.SaveSettings(settings);

            Patient first = db.AddPatient(SamplePatient(1, "محمد أحمد العسيري", "0500000001", "أبها"));
            Patient second = db.AddPatient(SamplePatient(2, "سارة عبدالله القحطاني", "0500000002", "خميس مشيط"));
            Patient third = db.AddPatient(SamplePatient(3, "خالد علي الشهري", "0500000003", "أبها"));
            DateTime appointmentTime = db.GetNextAvailableAppointmentTime(30);
            db.AddAppointment(new Appointment { PatientId = first.Id, Title = "مراجعة دورية", VisitType = "مراجعة", StartsAt = appointmentTime, DurationMinutes = 30, Status = "مؤكد" });
            db.AddTask(new PatientTask { PatientId = second.Id, Title = "الاتصال لتأكيد الموعد", DueAt = DateTime.Now.AddHours(3), Priority = "مرتفعة" });
            db.AddTask(new PatientTask { PatientId = third.Id, Title = "متابعة المستندات الناقصة", DueAt = DateTime.Now.AddDays(1), Priority = "عادية" });

            var window = new MainWindow(db, new BackupService(folder), security, local)
            {
                WindowState = WindowState.Normal,
                Width = Math.Max(980, requestedWidth),
                Height = Math.Max(700, requestedHeight),
                Left = 20,
                Top = 20,
                ShowInTaskbar = false
            };
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string? parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            using (var output = File.Create(outputPath)) encoder.Save(output);
            window.Close();
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 10_000 ? 0 : 2;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(outputPath + ".error.txt", ex.ToString()); } catch { }
            return 1;
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    private static Patient SamplePatient(int seed, string name, string mobile, string city)
    {
        string firstNine = "1" + seed.ToString("D8");
        int sum = 0;
        for (int i = 0; i < firstNine.Length; i++)
        {
            int digit = firstNine[i] - '0';
            if (i % 2 == 0) { int doubled = digit * 2; sum += doubled / 10 + doubled % 10; }
            else sum += digit;
        }
        string nationalId = firstNine + ((10 - sum % 10) % 10).ToString();
        return new Patient { IdentityType = "هوية وطنية", NationalId = nationalId, FullName = name, Gender = seed == 2 ? "أنثى" : "ذكر", DateOfBirth = new DateTime(1990 + seed, seed, Math.Min(10 + seed, 28)), Nationality = "سعودي", Mobile = mobile, City = city, BloodType = "غير محدد" };
    }

    private static void RemoveLegacyAutoStartEntries()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("SaudiPatientRecords", throwOnMissingValue: false);
            run?.DeleteValue("SaudiPatientRecordsV5", throwOnMissingValue: false);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogUnexpectedError(ex, "LegacyAutoStartCleanup");
        }
        catch (System.Security.SecurityException ex)
        {
            LogUnexpectedError(ex, "LegacyAutoStartCleanup");
        }
    }
}

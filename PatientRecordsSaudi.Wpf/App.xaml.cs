using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
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

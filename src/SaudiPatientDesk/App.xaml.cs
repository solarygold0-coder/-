using System.Globalization;
using System.IO;
using System.Windows;
using SaudiPatientDesk.Data;
using SaudiPatientDesk.Services;

namespace SaudiPatientDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var healthCheck = e.Args.Any(arg => string.Equals(arg, "--health-check", StringComparison.OrdinalIgnoreCase));
        var recoveryHealthCheck = e.Args.Any(arg =>
            string.Equals(arg, "--health-check-recovery", StringComparison.OrdinalIgnoreCase));
        var uiHealthCheck = e.Args.Any(arg =>
            string.Equals(arg, "--health-check-ui", StringComparison.OrdinalIgnoreCase));

        // واجهة عربية مع تقويم ميلادي حصراً. لا نستخدم تقويم أم القرى ضمنياً.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        culture.DateTimeFormat.ShortDatePattern = "yyyy/MM/dd";
        culture.DateTimeFormat.LongDatePattern = "yyyy/MM/dd";
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        DispatcherUnhandledException += (_, args) =>
        {
            var recoverable = args.Exception is InvalidOperationException or ArgumentException or FormatException;
            var diagnostic = WriteDiagnostic("runtime-error", args.Exception);
            MessageBox.Show(
                (recoverable ? "تعذر إكمال العملية، ويمكنك متابعة استخدام البرنامج." : "حدث خطأ خطير وسيُغلق البرنامج لحماية البيانات.") +
                "\n" + args.Exception.Message + "\n\nسجل التشخيص: " + diagnostic,
                "تنبيه", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = recoverable;
        };

        try
        {
            AppPaths.EnsureCreated();
            if (recoveryHealthCheck)
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAUDI_PATIENT_DESK_DATA_ROOT")))
                    throw new InvalidOperationException("اختبار الاستعادة يتطلب مسار بيانات اختبار معزولاً.");
                Database.CreateLegacyRecoveryFixture();
            }
            Database.Initialize(recoveryHealthCheck);
            if (healthCheck || recoveryHealthCheck)
            {
                Database.VerifyHealth();
                if (recoveryHealthCheck)
                    Database.VerifyLegacyRecoveryFixture();
                Shutdown(0);
                return;
            }
            Loc.Load(new SettingsService());
            MainWindow = new MainWindow();
            if (uiHealthCheck)
            {
                MainWindow.VerifyUiHealth();
                MainWindow.Close();
                Shutdown(0);
                return;
            }
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            if (healthCheck || recoveryHealthCheck || uiHealthCheck)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(AppPaths.Root, "health-error.txt"),
                        BuildDiagnostic(ex));
                }
                catch
                {
                }
                Shutdown(-1);
                return;
            }
            var errorFile = WriteDiagnostic("startup-error", ex);
            MessageBox.Show($"تعذر تشغيل البرنامج أو فتح قاعدة البيانات — الإصدار {AppInfo.Version}.\n" +
                            ex.Message + "\n\nسجل التشخيص: " + errorFile,
                "تعذر التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static string WriteDiagnostic(string prefix, Exception exception)
    {
        var errorFile = Path.Combine(AppPaths.Root, $"{prefix}-{AppInfo.Version}.txt");
        try { File.WriteAllText(errorFile, BuildDiagnostic(exception)); } catch { }
        return errorFile;
    }

    private static string BuildDiagnostic(Exception exception)
    {
        var text = exception + Environment.NewLine + Environment.NewLine + "آخر 20 عملية:" + Environment.NewLine;
        try
        {
            text += string.Join(Environment.NewLine,
                Database.RecentLog(20).Select(row => $"{row.At} | {row.Action} | {row.Detail}"));
        }
        catch (Exception auditError)
        {
            text += "تعذر قراءة سجل العمليات: " + auditError.Message;
        }
        return text;
    }
}

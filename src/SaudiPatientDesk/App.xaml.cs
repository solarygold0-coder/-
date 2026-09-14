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
            MessageBox.Show("حدث خطأ غير متوقع. لم تُفقد بياناتك.\n" + args.Exception.Message,
                "تنبيه", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            AppPaths.EnsureCreated();
            if (recoveryHealthCheck)
                Database.CreateLegacyRecoveryFixture();
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
                        ex.ToString());
                }
                catch
                {
                }
                Shutdown(-1);
                return;
            }
            var errorFile = Path.Combine(AppPaths.Root, $"startup-error-{AppInfo.Version}.txt");
            try { File.WriteAllText(errorFile, ex.ToString()); } catch { }
            MessageBox.Show($"تعذر تشغيل البرنامج أو فتح قاعدة البيانات — الإصدار {AppInfo.Version}.\n" +
                            ex.Message + "\n\nسجل التشخيص: " + errorFile,
                "تعذر التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}

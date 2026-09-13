using System.Globalization;
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
            Database.Initialize();
            if (healthCheck)
            {
                Database.VerifyHealth();
                Shutdown(0);
                return;
            }
            Loc.Load(new SettingsService());
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            if (healthCheck)
            {
                Shutdown(-1);
                return;
            }
            MessageBox.Show("تعذر تشغيل البرنامج أو فتح قاعدة البيانات.\n" + ex.Message,
                "تعذر التشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}

using System.Globalization;
using System.Windows;
using SaudiPatientDesk.Data;

namespace SaudiPatientDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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

        AppPaths.EnsureCreated();
        Database.Initialize();

        MainWindow = new MainWindow();
        MainWindow.Show();
        base.OnStartup(e);
    }
}

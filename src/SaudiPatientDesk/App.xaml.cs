using System.Globalization;
using System.Windows;
using SaudiPatientDesk.Data;

namespace SaudiPatientDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("ar-SA");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

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

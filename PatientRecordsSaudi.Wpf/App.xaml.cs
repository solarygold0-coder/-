using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PatientRecordsSaudi.Services;
using PatientRecordsSaudi.Wpf.Views;

namespace PatientRecordsSaudi.Wpf
{
    public partial class App : Application
    {
        private Mutex instanceMutex;
        public static string DataDirectory { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            bool first; instanceMutex = new Mutex(true, @"Local\SaudiPatientRecords_WPF_6E32AB7D_E9BC_4C0A_BA2D_222AF61EA4F8", out first);
            if (!first) { MessageBox.Show("البرنامج يعمل بالفعل.", "سجلات المراجعين", MessageBoxButton.OK, MessageBoxImage.Information); Shutdown(); return; }
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaudiPatientRecords");
            Directory.CreateDirectory(DataDirectory);
            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args) { Log(args.ExceptionObject as Exception, "Fatal"); };
            AppDatabase.CleanupTemporaryAttachments();
            try
            {
                var security = new AppSecurity(DataDirectory);
                var login = new LoginWindow(security);
                if (login.ShowDialog() != true) { Shutdown(); return; }
                var database = new AppDatabase(DataDirectory, login.Session.DatabasePassword, login.Session.DisplayName, login.Session.Role);
                security.FlushPendingAudit(database);
                var main = new MainWindow(database, new BackupService(DataDirectory), login.Session);
                MainWindow = main; main.Show();
            }
            catch (Exception ex) { Log(ex, "Startup"); MessageBox.Show("تعذر فتح بيانات البرنامج.\n\n" + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppDatabase.CleanupTemporaryAttachments();
            if (instanceMutex != null) instanceMutex.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception, "UI"); MessageBox.Show("حدث خطأ غير متوقع. حُفظت العمليات المكتملة.\n\n" + e.Exception.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true;
        }

        private static void Log(Exception ex, string area)
        {
            try { File.AppendAllText(Path.Combine(DataDirectory, "errors-wpf.log"), DateTime.UtcNow.ToString("O") + " | " + area + " | " + (ex == null ? "Unknown" : ex.ToString()) + Environment.NewLine + Environment.NewLine); } catch { }
        }
    }
}

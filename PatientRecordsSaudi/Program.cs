using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using PatientRecordsSaudi.Services;
using PatientRecordsSaudi.UI;

namespace PatientRecordsSaudi
{
    internal static class Program
    {
        public static string DataDirectory { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && Array.Exists(args, value => string.Equals(value, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = RunStandaloneSelfTest();
                return;
            }

            bool firstInstance;
            using (var instanceMutex = new Mutex(true, @"Local\SaudiPatientRecordsV5_64DD9F48_4C80_45A8_A169_F79CE8D6D211", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("البرنامج يعمل بالفعل. افتحه من أيقونته بجانب ساعة ويندوز.", "سجلات المراجعين", MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }

                ApplicationConfiguration.Initialize();
                Application.ApplicationExit += delegate { UiKit.DisposeResources(); };
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                DataDirectory = AppProfile.ResolveDataDirectory(localAppData);
                AppProfile.Initialize(DataDirectory);
                DisableLegacyAutoStart();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    LogUnexpectedError(e.Exception, "UI");
                    MessageBox.Show("حدث خطأ غير متوقع. حُفظت البيانات المكتملة ويمكنك متابعة العمل. إذا تكرر الخطأ أعد تشغيل البرنامج وراجع سجل الأخطاء.",
                        "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { LogUnexpectedError(e.ExceptionObject as Exception, "Fatal"); };
                AppDatabase.CleanupTemporaryAttachments();

                try
                {
                    var security = new AppSecurity(DataDirectory);
                    security.EnsureDefaultConfiguration();
                    SecuritySession activeSession = null;
                    if (!security.IsLoginRequired)
                    {
                        activeSession = security.OpenWithoutLogin();
                    }
                    else using (var login = new LoginForm(security, null, false)) { if (login.ShowDialog() != DialogResult.OK) return; activeSession = login.Session; }

                    if (activeSession == null) return;
                    using (activeSession)
                    using (var database = new AppDatabase(DataDirectory, activeSession.MaterializeDatabasePassword(), activeSession.DisplayName, activeSession.Role))
                    {
                        security.FlushPendingAudit(database);
                        Application.Run(new MainForm(database, new BackupService(DataDirectory), security, activeSession));
                    }
                }
                catch (Exception ex)
                {
                    LogUnexpectedError(ex, "Startup");
                    MessageBox.Show("تعذر فتح قاعدة البيانات. تأكد من كلمة المرور وسلامة ملفات البرنامج.\n\n" + ex.Message,
                        "خطأ في فتح البيانات", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
                finally { AppDatabase.CleanupTemporaryAttachments(); }
            }
        }

        private static int RunStandaloneSelfTest()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SaudiPatientRecordsStandalone_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var security = new AppSecurity(folder);
                if (!security.EnsureDefaultConfiguration()) return 3;
                if (security.IsLoginRequired) return 5;
                SecuritySession session = security.OpenWithoutLogin();
                using (session)
                using (var database = new AppDatabase(folder, session.MaterializeDatabasePassword(), session.DisplayName, session.Role))
                {
                    security.FlushPendingAudit(database);
                    if (!session.IsAdmin || database.CountActivePatients() != 0 || database.GetSettings().NextFileNumber != 1) return 2;
                    security.AddUser(session, "selfmanager", "مدير الفحص", "مدير", "test1234");
                    security.SetLoginRequired(session, true); if (!security.IsLoginRequired) return 6;
                    bool passwordlessBlocked = false; try { using (SecuritySession invalid = security.OpenWithoutLogin()) { } } catch (UnauthorizedAccessException) { passwordlessBlocked = true; } if (!passwordlessBlocked) return 7;
                    using (SecuritySession authenticated = security.Login("selfmanager", "test1234")) if (!authenticated.IsAdmin) return 8;
                    security.SetLoginRequired(session, false); using (SecuritySession reopened = security.OpenWithoutLogin()) if (!reopened.IsAdmin) return 9;
                    database.Checkpoint();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private static void DisableLegacyAutoStart()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (run != null) run.DeleteValue("SaudiPatientRecords", false);
            }
            catch { }
        }

        private static void LogUnexpectedError(Exception exception, string area)
        {
            try
            {
                string type = exception == null ? "Unknown" : exception.GetType().FullName;
                string entry = DateTime.UtcNow.ToString("O") + " | " + area + " | " + type + Environment.NewLine;
                File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), entry);
            }
            catch { }
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
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
            using (var instanceMutex = new Mutex(true, @"Local\SaudiPatientRecords_A7904157_8218_4708_9191_D6C477B3940C", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("البرنامج يعمل بالفعل. افتحه من أيقونته بجانب ساعة ويندوز.", "سجلات المراجعين", MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }

                ApplicationConfiguration.Initialize();
                DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaudiPatientRecords");
                Directory.CreateDirectory(DataDirectory);
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
                    using (var login = new LoginForm(security, null))
                    {
                        if (login.ShowDialog() != DialogResult.OK) return;
                        using (var database = new AppDatabase(DataDirectory, login.Session.DatabasePassword, login.Session.DisplayName, login.Session.Role))
                        {
                            security.FlushPendingAudit(database);
                            Application.Run(new MainForm(database, new BackupService(DataDirectory), security, login.Session));
                        }
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
                SecuritySession session = security.Configure("فحص التشغيل", "Standalone!2026");
                using (var database = new AppDatabase(folder, session.DatabasePassword, session.DisplayName, session.Role))
                {
                    security.FlushPendingAudit(database);
                    if (database.CountActivePatients() != 0 || database.GetSettings().NextFileNumber != 1) return 2;
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

        private static void LogUnexpectedError(Exception exception, string area)
        {
            try
            {
                string type = exception == null ? "Unknown" : exception.GetType().FullName;
                string stack = exception == null ? "" : exception.StackTrace ?? "";
                string entry = DateTime.UtcNow.ToString("O") + " | " + area + " | " + type + Environment.NewLine + stack + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), entry);
            }
            catch { }
        }
    }
}

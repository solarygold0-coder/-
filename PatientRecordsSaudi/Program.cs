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
        private const string CurrentDataFolderName = "SaudiPatientRecordsSecureV2";
        private const string LegacyDataFolderName = "SaudiPatientRecords";
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
            using (var instanceMutex = new Mutex(true, @"Local\SaudiPatientRecordsSecureV2_2AE74028_248D_4EE5_96FD_02DC05C303C6", out firstInstance))
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
                DataDirectory = Path.Combine(localAppData, CurrentDataFolderName);
                string legacyDataDirectory = Path.Combine(localAppData, LegacyDataFolderName);
                try { MigrateLegacyDataOnce(legacyDataDirectory, DataDirectory); }
                catch (Exception ex)
                {
                    LogUnexpectedError(ex, "Migration");
                    MessageBox.Show("تعذر عزل بيانات النسخة الجديدة وترحيل البيانات السابقة بأمان. أغلق أي نسخة قديمة ثم أعد تشغيل البرنامج. لم يتم تعديل بياناتك القديمة.\n\n" + ex.Message,
                        "تعذر ترحيل البيانات", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }
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
                    bool defaultCreated = security.EnsureDefaultConfiguration();
                    using (var login = new LoginForm(security, null, defaultCreated))
                    {
                        if (login.ShowDialog() != DialogResult.OK) return;
                        using (login.Session)
                        using (var database = new AppDatabase(DataDirectory, login.Session.MaterializeDatabasePassword(), login.Session.DisplayName, login.Session.Role))
                        {
                            security.FlushPendingAudit(database);
                            if (login.Session.UsesDefaultCredentials)
                                MessageBox.Show("أنت تستخدم بيانات الدخول الافتراضية admin / admin. غيّر كلمة المرور الآن من الإعدادات لحماية سجلات المراجعين.", "تنبيه أمني مهم", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
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
                string legacyProbe = Path.Combine(folder, "legacy-probe"), currentProbe = Path.Combine(folder, "current-probe"); Directory.CreateDirectory(legacyProbe); File.WriteAllText(Path.Combine(legacyProbe, "migration.test"), "ok");
                MigrateLegacyDataOnce(legacyProbe, currentProbe); if (File.ReadAllText(Path.Combine(currentProbe, "migration.test")) != "ok" || !File.Exists(Path.Combine(currentProbe, ".generation-v2"))) return 4;
                var security = new AppSecurity(folder);
                if (!security.EnsureDefaultConfiguration()) return 3;
                SecuritySession session = security.Login("admin", "admin");
                using (session)
                using (var database = new AppDatabase(folder, session.MaterializeDatabasePassword(), session.DisplayName, session.Role))
                {
                    security.FlushPendingAudit(database);
                    if (!session.IsAdmin || !session.UsesDefaultCredentials || database.CountActivePatients() != 0 || database.GetSettings().NextFileNumber != 1) return 2;
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

        private static void MigrateLegacyDataOnce(string legacyDirectory, string currentDirectory)
        {
            if (Directory.Exists(currentDirectory)) return;
            if (!Directory.Exists(legacyDirectory)) { Directory.CreateDirectory(currentDirectory); return; }

            bool legacyInstanceRunning;
            using (var legacyMutex = new Mutex(false, @"Local\SaudiPatientRecords_A7904157_8218_4708_9191_D6C477B3940C"))
            {
                try { legacyInstanceRunning = !legacyMutex.WaitOne(0); }
                catch (AbandonedMutexException) { legacyInstanceRunning = false; }
                if (legacyInstanceRunning) throw new InvalidOperationException("توجد نسخة سابقة تعمل الآن.");

                string stagingDirectory = currentDirectory + ".migrating";
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
                CopyDirectory(legacyDirectory, stagingDirectory);
                File.WriteAllText(Path.Combine(stagingDirectory, ".generation-v2"), "Saudi Patient Records secure data generation 2");
                Directory.Move(stagingDirectory, currentDirectory);
                try { legacyMutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
            foreach (string directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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

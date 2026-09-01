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
        private static void Main()
        {
            bool firstInstance;
            using (var instanceMutex = new Mutex(true, @"Local\SaudiPatientRecords_A7904157_8218_4708_9191_D6C477B3940C", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("البرنامج يعمل بالفعل. افتحه من أيقونته بجانب ساعة ويندوز.", "سجلات المراجعين", MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaudiPatientRecords");
                Directory.CreateDirectory(DataDirectory);
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
                    MessageBox.Show("تعذر فتح قاعدة البيانات. تأكد من كلمة المرور وسلامة ملفات البرنامج.\n\n" + ex.Message,
                        "خطأ في فتح البيانات", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
                finally { AppDatabase.CleanupTemporaryAttachments(); }
            }
        }
    }
}

using System;
using System.IO;
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaudiPatientRecords");
            Directory.CreateDirectory(DataDirectory);

            var security = new AppSecurity(DataDirectory);
            using (var login = new LoginForm(security, null))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var database = new AppDatabase(DataDirectory, login.Session.DatabasePassword, login.Session.DisplayName))
                        Application.Run(new MainForm(database, new BackupService(DataDirectory), security, login.Session));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("تعذر فتح قاعدة البيانات. تأكد من كلمة المرور وسلامة ملفات البرنامج.\n\n" + ex.Message,
                        "خطأ في فتح البيانات", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
            }
        }
    }
}

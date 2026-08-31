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
            using (var login = new LoginForm(security))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var database = new AppDatabase(DataDirectory, security.DatabasePassword(login.Password)))
                        Application.Run(new MainForm(database, new BackupService(DataDirectory)));
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

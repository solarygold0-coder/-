using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;

namespace PatientRecordsSaudi.Services
{
    public sealed class BackupService
    {
        private readonly string dataDirectory;
        public BackupService(string dataDirectory) { this.dataDirectory = dataDirectory; }

        public string CreateBackup(string destinationFolder, AppDatabase database)
        {
            Directory.CreateDirectory(destinationFolder);
            database.Checkpoint();
            string zip = Path.Combine(destinationFolder, "نسخة_سجلات_المرضى_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".zip");
            string temp = Path.Combine(Path.GetTempPath(), "PatientRecordsBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                CopyShared(Path.Combine(dataDirectory, "patients.db"), Path.Combine(temp, "patients.db"));
                string auth = Path.Combine(dataDirectory, "auth.dat");
                if (File.Exists(auth)) File.Copy(auth, Path.Combine(temp, "auth.dat"), true);
                File.WriteAllText(Path.Combine(temp, "README.txt"), "نسخة احتياطية مشفرة لنظام سجلات المرضى. لا تعدل محتوياتها.");
                ZipFile.CreateFromDirectory(temp, zip, CompressionLevel.Optimal, false);
                database.Audit("إنشاء نسخة احتياطية", "Backup", zip, null, Path.GetFileName(zip));
                return zip;
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        private static void CopyShared(string source, string destination)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
        }

        public void RestoreBackup(string zipPath, AppDatabase database)
        {
            string temp = Path.Combine(Path.GetTempPath(), "PatientRecordsRestore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, temp);
                string dbFile = Path.Combine(temp, "patients.db");
                string authFile = Path.Combine(temp, "auth.dat");
                if (!File.Exists(dbFile) || !File.Exists(authFile)) throw new InvalidDataException("ملف النسخة الاحتياطية غير صالح.");
                database.Close();
                string currentDb = Path.Combine(dataDirectory, "patients.db");
                string currentAuth = Path.Combine(dataDirectory, "auth.dat");
                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                string safetyDb = currentDb + ".before_restore_" + stamp;
                string safetyAuth = currentAuth + ".before_restore_" + stamp;
                if (File.Exists(currentDb)) File.Copy(currentDb, safetyDb, true);
                if (File.Exists(currentAuth)) File.Copy(currentAuth, safetyAuth, true);
                try
                {
                    File.Copy(dbFile, currentDb, true);
                    File.Copy(authFile, currentAuth, true);
                }
                catch
                {
                    if (File.Exists(safetyDb)) File.Copy(safetyDb, currentDb, true);
                    if (File.Exists(safetyAuth)) File.Copy(safetyAuth, currentAuth, true);
                    throw;
                }
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PatientRecordsSaudi.Services
{
    public sealed class BackupService
    {
        private readonly string dataDirectory;
        public BackupService(string dataDirectory) { this.dataDirectory = dataDirectory; }

        public string CreateBackup(string destinationFolder, AppDatabase database)
        {
            Directory.CreateDirectory(destinationFolder); database.Checkpoint();
            string zip = Path.Combine(destinationFolder, "نسخة_سجلات_المراجعين_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".zip");
            if (File.Exists(zip)) zip = Path.Combine(destinationFolder, "نسخة_سجلات_المراجعين_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            string temp = Path.Combine(Path.GetTempPath(), "PatientRecordsBackup_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                string dbFile = Path.Combine(temp, "patients.db"), authFile = Path.Combine(temp, "auth.dat");
                CopyShared(Path.Combine(dataDirectory, "patients.db"), dbFile); File.Copy(Path.Combine(dataDirectory, "auth.dat"), authFile, true);
                string manifest = "Format=SaudiPatientRecordsBackup-2\r\nCreatedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\r\nDatabaseSHA256=" + Hash(dbFile) + "\r\nAuthSHA256=" + Hash(authFile) + "\r\n";
                File.WriteAllText(Path.Combine(temp, "manifest.txt"), manifest, new UTF8Encoding(false));
                ZipFile.CreateFromDirectory(temp, zip, CompressionLevel.Optimal, false); database.Audit("إنشاء نسخة احتياطية", "Backup", Path.GetFileName(zip), null, Path.GetFileName(zip)); return zip;
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        public void RestoreBackup(string zipPath, AppDatabase database)
        {
            string temp = Path.Combine(Path.GetTempPath(), "PatientRecordsRestore_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                ExtractKnownFiles(zipPath, temp); string dbFile = Path.Combine(temp, "patients.db"), authFile = Path.Combine(temp, "auth.dat"), manifestFile = Path.Combine(temp, "manifest.txt");
                if (!File.Exists(dbFile) || !File.Exists(authFile)) throw new InvalidDataException("النسخة الاحتياطية غير مكتملة.");
                if (File.Exists(manifestFile))
                {
                    string manifest = File.ReadAllText(manifestFile, Encoding.UTF8); string dbHash = ManifestValue(manifest, "DatabaseSHA256"), authHash = ManifestValue(manifest, "AuthSHA256");
                    if (!string.Equals(dbHash, Hash(dbFile), StringComparison.OrdinalIgnoreCase) || !string.Equals(authHash, Hash(authFile), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("فشل فحص سلامة النسخة الاحتياطية؛ قد يكون الملف تالفًا أو معدلًا.");
                }
                database.ValidateDatabaseFile(dbFile); database.Close();
                string currentDb = Path.Combine(dataDirectory, "patients.db"), currentAuth = Path.Combine(dataDirectory, "auth.dat"), stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                string safetyDb = currentDb + ".before_restore_" + stamp, safetyAuth = currentAuth + ".before_restore_" + stamp; if (File.Exists(currentDb)) File.Copy(currentDb, safetyDb, true); if (File.Exists(currentAuth)) File.Copy(currentAuth, safetyAuth, true);
                try { File.Copy(dbFile, currentDb, true); File.Copy(authFile, currentAuth, true); }
                catch { if (File.Exists(safetyDb)) File.Copy(safetyDb, currentDb, true); if (File.Exists(safetyAuth)) File.Copy(safetyAuth, currentAuth, true); throw; }
                finally { TryDelete(safetyDb); TryDelete(safetyAuth); }
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        private static void ExtractKnownFiles(string zipPath, string destination)
        {
            string[] allowed = { "patients.db", "auth.dat", "manifest.txt" };
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/'); if (name.Contains("/") || !allowed.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    string target = Path.Combine(destination, name); using (Stream input = entry.Open()) using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }
        private static string ManifestValue(string text, string key) { string line = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal)); if (line == null) throw new InvalidDataException("بيانات سلامة النسخة ناقصة."); return line.Substring(key.Length + 1).Trim(); }
        private static string Hash(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        private static void CopyShared(string source, string destination) { using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output); }
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}

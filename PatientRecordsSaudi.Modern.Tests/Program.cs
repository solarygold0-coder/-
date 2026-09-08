using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using PatientRecordsSaudi.Models;
using PatientRecordsSaudi.Services;

namespace PatientRecordsSaudi.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "SaudiPatientRecordsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string error;
                string id1 = ValidNationalId(1), id2 = ValidNationalId(2), id3 = ValidNationalId(3), id4 = ValidNationalId(4);
                Assert(SaudiValidation.ValidateSaudiIdentity(ToArabicDigits(id1), "هوية وطنية", out error), "Arabic digit ID validation");
                string invalidId = id1.Substring(0, 9) + ((id1[9] - '0' + 1) % 10).ToString();
                Assert(!SaudiValidation.ValidateSaudiIdentity(invalidId, "هوية وطنية", out error), "ID check digit rejection");
                Assert(SaudiValidation.NormalizeDigits("١٢٣") == "123", "Digit normalization");
                Assert(SaudiValidation.NormalizeArabicName("  عَبْدُ الله أحمد ") == SaudiValidation.NormalizeArabicName("عبد الله احمد"), "Arabic name normalization");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 4)), "Friday rejected");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 5)), "Saturday rejected");
                Assert(AppDatabase.MaxPatients == 10000, "Administrative capacity is 10,000 patients");

                string defaultFolder = Path.Combine(temp, "default-login"); Directory.CreateDirectory(defaultFolder); var defaultSecurity = new AppSecurity(defaultFolder);
                Assert(defaultSecurity.EnsureDefaultConfiguration(), "Default admin configuration is created automatically");
                Assert(!defaultSecurity.IsLoginRequired, "Login screen is disabled on a fresh installation");
                using (SecuritySession passwordlessSession = defaultSecurity.OpenWithoutLogin()) Assert(passwordlessSession.IsAdmin, "Application opens without username or password by default");
                using (SecuritySession defaultSession = defaultSecurity.Login("admin", "admin"))
                {
                    Assert(defaultSession.IsAdmin && defaultSession.UsesDefaultCredentials, "Default admin/admin login works and is flagged for warning");
                    defaultSecurity.SetLoginRequired(defaultSession, true); Assert(defaultSecurity.IsLoginRequired, "Login can be enabled from settings");
                    bool passwordlessBlocked = false; try { using (SecuritySession denied = defaultSecurity.OpenWithoutLogin()) { } } catch (UnauthorizedAccessException) { passwordlessBlocked = true; } Assert(passwordlessBlocked, "Passwordless opening is blocked when login protection is enabled");
                    defaultSecurity.SetLoginRequired(defaultSession, false); Assert(!defaultSecurity.IsLoginRequired, "Login can be disabled again from settings");
                    defaultSecurity.ChangePassword(defaultSession, "admin", "safe1234"); Assert(!defaultSession.UsesDefaultCredentials, "Default credential warning clears after password change");
                }
                using (SecuritySession changedSession = defaultSecurity.Login("admin", "safe1234")) Assert(changedSession.IsAdmin, "Customized admin password works");

                string upgradeFolder = Path.Combine(temp, "upgrade-login"); Directory.CreateDirectory(upgradeFolder); var previousSecurity = new AppSecurity(upgradeFolder); previousSecurity.EnsureDefaultConfiguration();
                using (SecuritySession previousAdmin = previousSecurity.Login("admin", "admin")) previousSecurity.SetLoginRequired(previousAdmin, true);
                string upgradeAuthPath = Path.Combine(upgradeFolder, "auth.dat"); byte[] oldPlain = ProtectedData.Unprotect(File.ReadAllBytes(upgradeAuthPath), null, DataProtectionScope.CurrentUser); JsonNode oldStore = JsonNode.Parse(oldPlain); oldStore["StartupPolicyRevision"] = 0; File.WriteAllBytes(upgradeAuthPath, ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(oldStore.ToJsonString()), null, DataProtectionScope.CurrentUser)); CryptographicOperations.ZeroMemory(oldPlain);
                var upgradedSecurity = new AppSecurity(upgradeFolder); Assert(upgradedSecurity.IsLoginRequired, "Previous installation can contain inherited startup login"); Assert(upgradedSecurity.EnsureDirectStartupForCurrentRelease(), "Upgrade disables inherited startup login exactly once"); Assert(!upgradedSecurity.IsLoginRequired, "Upgraded application opens directly");
                using (SecuritySession upgradedAdmin = upgradedSecurity.OpenWithoutLogin()) { upgradedSecurity.SetLoginRequired(upgradedAdmin, true); Assert(!upgradedSecurity.EnsureDirectStartupForCurrentRelease() && upgradedSecurity.IsLoginRequired, "Later administrator choice to enable login is preserved"); }

                string adminPassword = "test1234", employeePassword = "empl1234";
                var security = new AppSecurity(temp); bool invalidPasswordBlocked = false; try { security.Configure("مدير الاختبار", "UPPER1"); } catch (ArgumentException) { invalidPasswordBlocked = true; } Assert(invalidPasswordBlocked, "Custom password policy rejects uppercase and requires lowercase letters plus digits"); SecuritySession admin = security.Configure("مدير الاختبار", adminPassword);
                bool overLimitBlocked = false; try { security.AddUser(admin, "baduser", "مستخدم غير صالح", "موظف", "abcde1234"); } catch (ArgumentException) { overLimitBlocked = true; } Assert(overLimitBlocked, "Custom password policy enforces four-letter maximum");
                security.AddUser(admin, "employee", "موظف الاختبار", "موظف", employeePassword); SecuritySession employee = security.Login("employee", employeePassword);
                Assert(employee.DisplayName == "موظف الاختبار" && !employee.IsAdmin, "Per-user login and role");
                string retryPassword = "lock1234", wrongPassword = "fail1234"; security.AddUser(admin, "retrytest", "اختبار تكرار المحاولة", "موظف", retryPassword); for (int i = 0; i < 12; i++) try { security.Login("retrytest", wrongPassword); } catch (UnauthorizedAccessException) { }
                using (SecuritySession retrySession = security.Login("retrytest", retryPassword)) Assert(!retrySession.IsAdmin, "Repeated incorrect passwords never disable or lock the application account");
                Assert(!File.Exists(Path.Combine(temp, "auth.dat.bak")), "Obsolete authentication backup is not retained");
                Assert(File.ReadAllBytes(Path.Combine(temp, "auth.dat"))[0] != (byte)'{', "Authentication store is protected with Windows DPAPI");

                using (var db = new AppDatabase(temp, admin.MaterializeDatabasePassword(), admin.DisplayName))
                {
                    Assert(db.DatabasePath.EndsWith("patients.sqlite3", StringComparison.OrdinalIgnoreCase), "Active database is SQLite");
                    security.FlushPendingAudit(db); Assert(db.GetRecentAudit(100).Exists(x => x.EntityType == "Security"), "Security events are imported into audit log");
                    Patient one = db.AddPatient(NewPatient(id1, "مراجع الاختبار الأول", TestMobile(1)));
                    Patient two = db.AddPatient(NewPatient(id2, "مراجع الاختبار الثاني", TestMobile(2)));
                    Assert(one.FileNumber == 1 && two.FileNumber == 2, "Sequential file numbering starts at 1");
                    AppSettings settings = db.GetSettings(); Assert(settings.VisitTypes.Count > 0 && settings.AppointmentStatuses.Contains("حضر"), "Default configurable lookups"); settings.VisitTypes.Add("زيارة اختبار"); db.SaveSettings(settings); Assert(db.GetSettings().VisitTypes.Contains("زيارة اختبار"), "Lookup customization persisted");
                    string attachmentSource = Path.Combine(temp, "test.pdf"); File.WriteAllText(attachmentSource, "%PDF-1.4 test attachment"); PatientAttachment attachment = db.AddAttachment(two.Id, attachmentSource, "نتيجة");
                    Assert(db.GetAttachments(two.Id, false).Count == 1 && attachment.SizeBytes > 0, "Encrypted attachment stored in database"); string attachmentCopy = db.ExportAttachmentToTemporaryFile(attachment.Id); Assert(File.ReadAllText(attachmentCopy) == "%PDF-1.4 test attachment", "Attachment integrity verified on open"); db.DeleteAttachment(attachment.Id); Assert(db.GetAttachments(two.Id, false).Count == 0, "Attachment soft delete"); db.RestoreAttachment(attachment.Id); Assert(db.GetAttachments(two.Id, false).Count == 1, "Attachment restore");
                    string fakeImage = Path.Combine(temp, "fake.jpg"); File.WriteAllText(fakeImage, "not a jpeg"); bool fakeBlocked = false; try { db.AddAttachment(two.Id, fakeImage, "أخرى"); } catch (InvalidDataException) { fakeBlocked = true; } Assert(fakeBlocked, "Attachment content must match extension");
                    db.ArchivePatient(one.Id, "اختبار");
                    Patient archived = db.GetPatient(one.Id); archived.City = "مدينة معدلة"; bool archivedEditBlocked = false; try { db.UpdatePatient(archived); } catch (InvalidOperationException) { archivedEditBlocked = true; } Assert(archivedEditBlocked, "Archived patient cannot be edited before restore");
                    Patient three = db.AddPatient(NewPatient(id3, "مراجع الاختبار الثالث", TestMobile(3)));
                    Assert(three.FileNumber == 3, "Deleted/archived number is not reused");
                    DateTime firstAvailable = db.GetNextAvailableAppointmentTime(30);
                    Assert(firstAvailable >= DateTime.Now.AddMinutes(-1), "Next available appointment is not in the past");
                    Assert(SaudiValidation.IsOfficialWorkingDay(firstAvailable), "Next available appointment respects Saudi working days");
                    bool duplicateBlocked = false;
                    try { db.AddPatient(NewPatient(id2, "اسم آخر", TestMobile(4))); } catch (InvalidOperationException) { duplicateBlocked = true; }
                    Assert(duplicateBlocked, "Duplicate national ID blocked");

                    int daysToSunday = ((int)DayOfWeek.Sunday - (int)DateTime.Today.DayOfWeek + 7) % 7; if (daysToSunday == 0) daysToSunday = 7; DateTime sunday = DateTime.Today.AddDays(daysToSunday).AddHours(9);
                    Appointment saved = db.AddAppointment(new Appointment { PatientId = two.Id, FileNumber = two.FileNumber, PatientName = two.FullName, Title = "مراجعة", VisitType = "مراجعة", StartsAt = sunday, DurationMinutes = 30, Status = "مؤكد" });
                    db.MarkAppointmentNotified(saved.Id); Appointment statusChanged = db.GetAppointment(saved.Id); statusChanged.Status = "بانتظار التأكيد"; db.UpdateAppointment(statusChanged);
                    Assert(db.GetAppointment(saved.Id).ReminderNotifiedAt == null, "Appointment reminder resets when an active status changes");
                    bool conflictBlocked = false;
                    try { db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "متعارض", VisitType = "مراجعة", StartsAt = sunday.AddMinutes(15), DurationMinutes = 30, Status = "مؤكد" }); } catch (AppointmentConflictException) { conflictBlocked = true; }
                    Assert(conflictBlocked, "Overlapping appointment blocked");
                    Appointment cancelled = db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "ملغي", VisitType = "مراجعة", StartsAt = sunday.AddMinutes(15), DurationMinutes = 30, Status = "ملغي" }); Assert(cancelled != null, "Cancelled appointment does not reserve the slot");
                    PatientTask reminderTask = db.AddTask(new PatientTask { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "متابعة", DueAt = sunday, Priority = "عادية" });
                    db.MarkTaskNotified(reminderTask.Id); PatientTask completedTask = db.GetPatientTasks(three.Id).First(x => x.Id == reminderTask.Id); completedTask.IsCompleted = true; db.UpdateTask(completedTask);
                    PatientTask reopenedTask = db.GetPatientTasks(three.Id).First(x => x.Id == reminderTask.Id); reopenedTask.IsCompleted = false; db.UpdateTask(reopenedTask);
                    Assert(db.GetPatientTasks(three.Id).First(x => x.Id == reminderTask.Id).ReminderNotifiedAt == null, "Task reminder resets when a task is reopened");
                    db.DeleteAppointment(saved.Id); Assert(db.GetDeletedAppointments().Count == 1 && db.GetAppointments(null, null).Count == 1, "Appointment soft delete");
                    db.RestoreAppointment(saved.Id); Assert(db.GetAppointments(null, null).Count == 2, "Appointment restore");
                    db.ArchivePatient(two.Id, "اختبار", true); Assert(db.GetAppointment(saved.Id).Status == "ملغي", "Archiving closes future appointments");
                    DateTime monday = sunday.AddDays(1); db.AddClosure(monday, "إجازة اختبار"); bool closureBlocked = false;
                    try { db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "إجازة", VisitType = "مراجعة", StartsAt = monday, DurationMinutes = 30, Status = "مؤكد" }); } catch (InvalidOperationException) { closureBlocked = true; }
                    Assert(closureBlocked, "Configured closure date blocks appointments");
                    db.DeleteAttachment(attachment.Id); Assert(db.PurgeDeletedAttachments(DateTime.Now.AddDays(1)) == 1 && db.GetAttachments(two.Id, true).Count == 0, "Old deleted attachments can be permanently purged by admin");
                    string backupFolder = Path.Combine(temp, "backup-test"); string backup = new BackupService(temp).CreateBackup(backupFolder, db);
                    using (ZipArchive archive = ZipFile.OpenRead(backup)) Assert(archive.GetEntry("patients.sqlite3") != null && archive.GetEntry("database.key") != null && archive.GetEntry("manifest.txt") != null, "Consistent SQLite backup contains database, protected key and integrity manifest");
                    db.Checkpoint(); byte[] header = new byte[16]; using (FileStream input = new FileStream(db.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) input.ReadExactly(header);
                    Assert(System.Text.Encoding.ASCII.GetString(header) != "SQLite format 3\0", "SQLite file header is encrypted by SQLCipher");
                }
                bool wrongDatabaseKeyBlocked = false; try { using (var wrong = new AppDatabase(temp, "wrong-key", "اختبار")) { } } catch { wrongDatabaseKeyBlocked = true; } Assert(wrongDatabaseKeyBlocked, "Wrong SQLite encryption key is rejected");
                AppDatabase.CleanupTemporaryAttachments(); Assert(!Directory.EnumerateDirectories(Path.GetTempPath(), "SaudiPatientRecordsView_*").Any(), "Decrypted temporary attachments are removed");
                using (var readOnlyDb = new AppDatabase(temp, admin.MaterializeDatabasePassword(), employee.DisplayName, "قراءة فقط"))
                {
                    bool writeBlocked = false; try { readOnlyDb.AddPatient(NewPatient(id4, "مراجع للقراءة فقط", TestMobile(4))); } catch (UnauthorizedAccessException) { writeBlocked = true; } Assert(writeBlocked, "Read-only role is enforced in data layer");
                }
                using (var staffDb = new AppDatabase(temp, admin.MaterializeDatabasePassword(), employee.DisplayName, "موظف"))
                {
                    bool settingsBlocked = false; try { staffDb.SaveSettings(staffDb.GetSettings()); } catch (UnauthorizedAccessException) { settingsBlocked = true; } Assert(settingsBlocked, "Admin-only settings are enforced in data layer");
                }
                RunTenThousandCapacityTest(temp);
                employee.Dispose(); admin.Dispose();
                Console.WriteLine("All checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        private static Patient NewPatient(string id, string name, string mobile)
        {
            return new Patient { IdentityType = "هوية وطنية", NationalId = id, FullName = name, Gender = "ذكر", DateOfBirth = new DateTime(1990, 1, 1), Nationality = "سعودي", Mobile = mobile, City = "الرياض", BloodType = "غير محدد" };
        }
        private static string TestMobile(int value) { return "05" + value.ToString("D8"); }
        private static string ToArabicDigits(string value) { string latin = "0123456789", arabic = "٠١٢٣٤٥٦٧٨٩"; char[] result = value.ToCharArray(); for (int i = 0; i < result.Length; i++) result[i] = arabic[latin.IndexOf(result[i])]; return new string(result); }
        private static string ValidNationalId(int seed)
        {
            string firstNine = "1" + seed.ToString("D8"); int sum = 0;
            for (int i = 0; i < 9; i++) { int digit = firstNine[i] - '0'; if (i % 2 == 0) { int doubled = digit * 2; sum += doubled / 10 + doubled % 10; } else sum += digit; }
            return firstNine + ((10 - sum % 10) % 10).ToString();
        }
        private static void RunTenThousandCapacityTest(string root)
        {
            string folder = Path.Combine(root, "capacity"); Directory.CreateDirectory(folder); string password = "Capacity-" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(folder, "patients.sqlite3"); AppSettings settings;
            using (var schema = new AppDatabase(folder, password, "اختبار السعة")) { settings = schema.GetSettings(); schema.Close(); }
            SQLitePCL.Batteries_V2.Init();
            string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Private, Pooling = false, ForeignKeys = true, Password = password }.ToString();
            using (var sqlite = new SqliteConnection(connectionString))
            {
                sqlite.Open(); using SqliteTransaction tx = sqlite.BeginTransaction(); using SqliteCommand insert = sqlite.CreateCommand(); insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO patients(id,file_number,national_id,normalized_name,mobile,city,date_of_birth_ticks,created_ticks,last_visit_ticks,is_archived,payload)
VALUES($id,$file,$national,$name,$mobile,$city,NULL,$created,NULL,0,$payload);";
                insert.Parameters.Add("$id", SqliteType.Text); insert.Parameters.Add("$file", SqliteType.Integer); insert.Parameters.Add("$national", SqliteType.Text); insert.Parameters.Add("$name", SqliteType.Text); insert.Parameters.Add("$mobile", SqliteType.Text); insert.Parameters.Add("$city", SqliteType.Text); insert.Parameters.Add("$created", SqliteType.Integer); insert.Parameters.Add("$payload", SqliteType.Text);
                var json = new JsonSerializerOptions { IgnoreReadOnlyProperties = true };
                for (int i = 1; i <= 10000; i++)
                {
                    var patient = new Patient { Id = Guid.NewGuid(), FileNumber = i, NationalId = "T" + i.ToString("D9"), FullName = "مراجع سعة " + i, NormalizedName = "مراجع سعه " + i, Mobile = "M" + i.ToString("D9"), City = "اختبار", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
                    insert.Parameters["$id"].Value = patient.Id.ToString("N"); insert.Parameters["$file"].Value = i; insert.Parameters["$national"].Value = patient.NationalId; insert.Parameters["$name"].Value = patient.NormalizedName; insert.Parameters["$mobile"].Value = patient.Mobile; insert.Parameters["$city"].Value = patient.City; insert.Parameters["$created"].Value = patient.CreatedAt.Ticks; insert.Parameters["$payload"].Value = JsonSerializer.Serialize(patient, json); insert.ExecuteNonQuery();
                }
                settings.NextFileNumber = 10001; settings.ClinicName = "اختبار السعة"; settings.UpdatedAt = DateTime.Now; using SqliteCommand update = sqlite.CreateCommand(); update.Transaction = tx; update.CommandText = "UPDATE settings SET payload=$payload WHERE id=1;"; update.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settings, json)); update.ExecuteNonQuery(); tx.Commit();
            }
            using (var database = new AppDatabase(folder, password, "اختبار السعة"))
            {
                Assert(database.CountAllPatients() == 10000, "10,000 records stored and reopened");
                Assert(database.FindByFileNumber(10000, false) != null, "Indexed lookup at record 10,000");
                Assert(database.SearchPatients("الاسم", "مراجع سعة 9999", false, "رقم الملف").Count == 1, "Arabic name search across 10,000 records");
            }
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS: " + name); }
    }
}

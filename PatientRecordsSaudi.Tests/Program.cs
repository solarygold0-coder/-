using System;
using System.Collections.Generic;
using System.IO;
using LiteDB;
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
                string id1 = ValidNationalId(1), id2 = ValidNationalId(2), id3 = ValidNationalId(3);
                Assert(SaudiValidation.ValidateSaudiIdentity(ToArabicDigits(id1), "هوية وطنية", out error), "Arabic digit ID validation");
                string invalidId = id1.Substring(0, 9) + ((id1[9] - '0' + 1) % 10).ToString();
                Assert(!SaudiValidation.ValidateSaudiIdentity(invalidId, "هوية وطنية", out error), "ID check digit rejection");
                Assert(SaudiValidation.NormalizeDigits("١٢٣") == "123", "Digit normalization");
                Assert(SaudiValidation.NormalizeArabicName("  عَبْدُ الله أحمد ") == SaudiValidation.NormalizeArabicName("عبد الله احمد"), "Arabic name normalization");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 4)), "Friday rejected");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 5)), "Saturday rejected");
                Assert(AppDatabase.MaxPatients == 10000, "Administrative capacity is 10,000 patients");

                string adminPassword = "A!" + Guid.NewGuid().ToString("N"), employeePassword = "E!" + Guid.NewGuid().ToString("N");
                var security = new AppSecurity(temp); SecuritySession admin = security.Configure("مدير الاختبار", adminPassword);
                security.AddUser(admin, "employee", "موظف الاختبار", "موظف", employeePassword); SecuritySession employee = security.Login("employee", employeePassword);
                Assert(employee.DisplayName == "موظف الاختبار" && !employee.IsAdmin, "Per-user login and role");

                using (var db = new AppDatabase(temp, admin.DatabasePassword, admin.DisplayName))
                {
                    Patient one = db.AddPatient(NewPatient(id1, "مراجع الاختبار الأول", TestMobile(1)));
                    Patient two = db.AddPatient(NewPatient(id2, "مراجع الاختبار الثاني", TestMobile(2)));
                    Assert(one.FileNumber == 1 && two.FileNumber == 2, "Sequential file numbering starts at 1");
                    db.ArchivePatient(one.Id, "اختبار");
                    Patient three = db.AddPatient(NewPatient(id3, "مراجع الاختبار الثالث", TestMobile(3)));
                    Assert(three.FileNumber == 3, "Deleted/archived number is not reused");
                    bool duplicateBlocked = false;
                    try { db.AddPatient(NewPatient(id2, "اسم آخر", TestMobile(4))); } catch (InvalidOperationException) { duplicateBlocked = true; }
                    Assert(duplicateBlocked, "Duplicate national ID blocked");

                    DateTime sunday = new DateTime(2026, 9, 6, 9, 0, 0);
                    Appointment saved = db.AddAppointment(new Appointment { PatientId = two.Id, FileNumber = two.FileNumber, PatientName = two.FullName, Title = "مراجعة", VisitType = "مراجعة", StartsAt = sunday, DurationMinutes = 30, Status = "مؤكد" });
                    bool conflictBlocked = false;
                    try { db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "متعارض", VisitType = "مراجعة", StartsAt = sunday.AddMinutes(15), DurationMinutes = 30, Status = "مؤكد" }); } catch (AppointmentConflictException) { conflictBlocked = true; }
                    Assert(conflictBlocked, "Overlapping appointment blocked");
                    db.DeleteAppointment(saved.Id); Assert(db.GetDeletedAppointments().Count == 1 && db.GetAppointments(null, null).Count == 0, "Appointment soft delete");
                    db.RestoreAppointment(saved.Id); Assert(db.GetAppointments(null, null).Count == 1, "Appointment restore");
                    db.ArchivePatient(two.Id, "اختبار", true); Assert(db.GetAppointment(saved.Id).Status == "ملغي", "Archiving closes future appointments");
                    db.AddClosure(new DateTime(2026, 9, 7), "إجازة اختبار"); bool closureBlocked = false;
                    try { db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "إجازة", VisitType = "مراجعة", StartsAt = new DateTime(2026, 9, 7, 9, 0, 0), DurationMinutes = 30, Status = "مؤكد" }); } catch (InvalidOperationException) { closureBlocked = true; }
                    Assert(closureBlocked, "Configured closure date blocks appointments");
                }
                RunTenThousandCapacityTest(temp);
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
            string path = Path.Combine(folder, "patients.db");
            using (var lite = new LiteDatabase(new ConnectionString { Filename = path, Password = password, Connection = ConnectionType.Direct }))
            {
                var patients = lite.GetCollection<Patient>("patients"); var batch = new List<Patient>(1000);
                for (int i = 1; i <= 10000; i++)
                {
                    batch.Add(new Patient { Id = Guid.NewGuid(), FileNumber = i, NationalId = "T" + i.ToString("D9"), FullName = "مراجع سعة " + i, NormalizedName = "مراجع سعه " + i, Mobile = "M" + i.ToString("D9"), City = "اختبار", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
                    if (batch.Count == 1000) { patients.InsertBulk(batch); batch.Clear(); }
                }
                lite.GetCollection<AppSettings>("settings").Insert(new AppSettings { Id = 1, NextFileNumber = 10001, ClinicName = "اختبار السعة", DefaultAppointmentMinutes = 30, WorkDayStartMinutes = 480, WorkDayEndMinutes = 1020, BackupIntervalHours = 4, UpdatedAt = DateTime.Now });
                lite.Checkpoint();
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

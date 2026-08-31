using System;
using System.IO;
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
                Assert(SaudiValidation.ValidateSaudiIdentity("١٠٠٠٠٠٠٠٠٨", "هوية وطنية", out error), "Arabic digit ID validation");
                Assert(!SaudiValidation.ValidateSaudiIdentity("1000000009", "هوية وطنية", out error), "ID check digit rejection");
                Assert(SaudiValidation.NormalizeDigits("١٢٣") == "123", "Digit normalization");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 4)), "Friday rejected");
                Assert(!SaudiValidation.IsOfficialWorkingDay(new DateTime(2026, 9, 5)), "Saturday rejected");

                using (var db = new AppDatabase(temp, "test-password-key"))
                {
                    Patient one = db.AddPatient(NewPatient("1000000008", "مراجع الاختبار الأول", "0500000001"));
                    Patient two = db.AddPatient(NewPatient("1000000016", "مراجع الاختبار الثاني", "0500000002"));
                    Assert(one.FileNumber == 1 && two.FileNumber == 2, "Sequential file numbering starts at 1");
                    db.ArchivePatient(one.Id, "اختبار");
                    Patient three = db.AddPatient(NewPatient("1000000024", "مراجع الاختبار الثالث", "0500000003"));
                    Assert(three.FileNumber == 3, "Deleted/archived number is not reused");
                    bool duplicateBlocked = false;
                    try { db.AddPatient(NewPatient("1000000016", "اسم آخر", "0500000004")); } catch (InvalidOperationException) { duplicateBlocked = true; }
                    Assert(duplicateBlocked, "Duplicate national ID blocked");

                    DateTime sunday = new DateTime(2026, 9, 6, 9, 0, 0);
                    db.AddAppointment(new Appointment { PatientId = two.Id, FileNumber = two.FileNumber, PatientName = two.FullName, Title = "مراجعة", VisitType = "مراجعة", StartsAt = sunday, DurationMinutes = 30, Status = "مؤكد" });
                    bool conflictBlocked = false;
                    try { db.AddAppointment(new Appointment { PatientId = three.Id, FileNumber = three.FileNumber, PatientName = three.FullName, Title = "متعارض", VisitType = "مراجعة", StartsAt = sunday.AddMinutes(15), DurationMinutes = 30, Status = "مؤكد" }); } catch (AppointmentConflictException) { conflictBlocked = true; }
                    Assert(conflictBlocked, "Overlapping appointment blocked");
                }
                Console.WriteLine("All checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        private static Patient NewPatient(string id, string name, string mobile)
        {
            return new Patient { IdentityType = "هوية وطنية", NationalId = id, FullName = name, Gender = "ذكر", DateOfBirth = new DateTime(1990, 1, 1), Nationality = "سعودي", Mobile = mobile, City = "الرياض", BloodType = "غير محدد" };
        }
        private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS: " + name); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using PatientRecordsSaudi.Models;

namespace PatientRecordsSaudi.Services
{
    public sealed partial class AppDatabase
    {
        private void ImportLegacyDatabaseIfNeeded()
        {
            string legacyPath = Path.Combine(DataDirectory, "patients.db");
            if (!File.Exists(legacyPath) || ScalarLong("SELECT COUNT(*) FROM meta WHERE key='legacy_litedb_imported';") > 0) return;
            if (CountAllPatients() > 0) { Execute("INSERT OR REPLACE INTO meta(key,value) VALUES('legacy_litedb_imported','skipped_nonempty');"); return; }

            try
            {
                using (var legacy = new LiteDatabase(new ConnectionString { Filename = legacyPath, Password = MaterializeDatabasePassword(), Connection = ConnectionType.Direct, ReadOnly = true, Upgrade = false }))
                {
                    List<Patient> patients = legacy.GetCollection<Patient>("patients").FindAll().OrderBy(x => x.FileNumber).ToList();
                    List<Appointment> appointments = legacy.GetCollection<Appointment>("appointments").FindAll().ToList();
                    List<PatientTask> tasks = legacy.GetCollection<PatientTask>("tasks").FindAll().ToList();
                    List<PatientAttachment> attachments = legacy.GetCollection<PatientAttachment>("attachments").FindAll().ToList();
                    List<ClosureDate> closures = legacy.GetCollection<ClosureDate>("closures").FindAll().ToList();
                    List<AuditEntry> audit = legacy.GetCollection<AuditEntry>("audit").FindAll().OrderByDescending(x => x.OccurredAt).Take(MaxAuditEntries).OrderBy(x => x.OccurredAt).ToList();
                    AppSettings oldSettings = legacy.GetCollection<AppSettings>("settings").FindById(1);

                    InTransaction(() =>
                    {
                        foreach (Patient patient in patients)
                        {
                            if (patient.Id == Guid.Empty) patient.Id = Guid.NewGuid();
                            if (string.IsNullOrWhiteSpace(patient.NormalizedName)) patient.NormalizedName = SaudiValidation.NormalizeArabicName(patient.FullName);
                            patient.NationalId = SaudiValidation.NormalizeDigits(patient.NationalId); patient.Mobile = SaudiValidation.NormalizeSaudiMobile(patient.Mobile); patient.City = patient.City ?? "";
                            InsertPatient(patient);
                        }
                        foreach (Appointment appointment in appointments) { if (appointment.Id == Guid.Empty) appointment.Id = Guid.NewGuid(); InsertAppointment(appointment); }
                        foreach (PatientTask task in tasks) { if (task.Id == Guid.Empty) task.Id = Guid.NewGuid(); InsertTask(task); }
                        foreach (PatientAttachment attachment in attachments)
                        {
                            if (attachment.Id == Guid.Empty) attachment.Id = Guid.NewGuid();
                            LiteFileInfo<string> stored = legacy.FileStorage.FindById(attachment.StoredId); if (stored == null) continue;
                            using (var output = new MemoryStream()) { stored.CopyTo(output); byte[] content = output.ToArray(); if (string.IsNullOrWhiteSpace(attachment.Sha256)) attachment.Sha256 = HashBytes(content); attachment.StoredId = "sqlite:" + attachment.Id.ToString("N"); InsertAttachment(attachment, content); }
                        }
                        foreach (ClosureDate closure in closures) { if (closure.Id == Guid.Empty) closure.Id = Guid.NewGuid(); Execute("INSERT OR IGNORE INTO closures(id,date_ticks,payload) VALUES($id,$date,$payload);", ("$id", closure.Id.ToString("N")), ("$date", closure.Date.Date.Ticks), ("$payload", Serialize(closure))); }
                        foreach (AuditEntry entry in audit) { if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid(); InsertAudit(entry); }
                        if (oldSettings != null)
                        {
                            EnsureSettingsDefaults(oldSettings); long highest = patients.Count == 0 ? 0 : patients.Max(x => x.FileNumber); if (oldSettings.NextFileNumber <= highest) oldSettings.NextFileNumber = highest + 1; oldSettings.Id = 1; oldSettings.UpdatedAt = DateTime.Now; UpsertSettingsInternal(oldSettings);
                            if (!string.IsNullOrWhiteSpace(oldSettings.ClinicLogoStoredId))
                            {
                                LiteFileInfo<string> logo = legacy.FileStorage.FindById(oldSettings.ClinicLogoStoredId); if (logo != null) using (var output = new MemoryStream()) { logo.CopyTo(output); Execute("INSERT OR REPLACE INTO assets(key,content) VALUES('clinic_logo',$content);", ("$content", output.ToArray())); }
                            }
                        }
                        Execute("INSERT OR REPLACE INTO meta(key,value) VALUES('legacy_litedb_imported',$value);", ("$value", DateTime.UtcNow.ToString("O")));
                        AuditInternal("ترحيل قاعدة البيانات", "System", "SQLite", null, "تم استيراد " + patients.Count + " مراجع من قاعدة LiteDB السابقة إلى SQLite المشفرة.");
                    }, true);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("تعذر استيراد قاعدة البيانات السابقة إلى SQLite. بقي ملف patients.db القديم دون تعديل. " + ex.Message, ex);
            }
        }
    }
}

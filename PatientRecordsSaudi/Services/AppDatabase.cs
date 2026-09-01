using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using PatientRecordsSaudi.Models;

namespace PatientRecordsSaudi.Services
{
    public sealed class AppDatabase : IDisposable
    {
        public const int MaxPatients = 10000;
        private LiteDatabase db; private readonly string databasePassword; private string currentUser;
        public string DataDirectory { get; private set; }
        public string DatabasePath { get; private set; }

        public AppDatabase(string dataDirectory, string password) : this(dataDirectory, password, "النظام") { }
        public AppDatabase(string dataDirectory, string password, string user)
        {
            DataDirectory = dataDirectory; Directory.CreateDirectory(DataDirectory); DatabasePath = Path.Combine(DataDirectory, "patients.db"); databasePassword = password; currentUser = user ?? "النظام"; Open();
        }
        public void SetCurrentUser(string user) { currentUser = string.IsNullOrWhiteSpace(user) ? "النظام" : user.Trim(); }

        private void Open()
        {
            db = new LiteDatabase(new ConnectionString { Filename = DatabasePath, Password = databasePassword, Connection = ConnectionType.Shared, Upgrade = false }); EnsureSchema();
        }

        private void EnsureSchema()
        {
            var patients = db.GetCollection<Patient>("patients"); patients.EnsureIndex(x => x.FileNumber, true); patients.EnsureIndex(x => x.NationalId, true); patients.EnsureIndex(x => x.FullName); patients.EnsureIndex(x => x.NormalizedName); patients.EnsureIndex(x => x.Mobile); patients.EnsureIndex(x => x.IsArchived);
            var appointments = db.GetCollection<Appointment>("appointments"); appointments.EnsureIndex(x => x.PatientId); appointments.EnsureIndex(x => x.FileNumber); appointments.EnsureIndex(x => x.StartsAt); appointments.EnsureIndex(x => x.IsDeleted);
            var tasks = db.GetCollection<PatientTask>("tasks"); tasks.EnsureIndex(x => x.PatientId); tasks.EnsureIndex(x => x.FileNumber); tasks.EnsureIndex(x => x.DueAt); tasks.EnsureIndex(x => x.IsDeleted);
            db.GetCollection<ClosureDate>("closures").EnsureIndex(x => x.Date, true);
            var settings = db.GetCollection<AppSettings>("settings"); AppSettings s = settings.FindById(1);
            if (s == null) settings.Insert(new AppSettings { Id = 1, NextFileNumber = 1, ClinicName = "المنشأة", DefaultAppointmentMinutes = 30, WorkDayStartMinutes = 8 * 60, WorkDayEndMinutes = 17 * 60, BackupIntervalHours = 4, LastBackupStatus = "لم تُنشأ نسخة بعد", UpdatedAt = DateTime.Now });
            else
            {
                bool changed = false; if (s.WorkDayStartMinutes <= 0) { s.WorkDayStartMinutes = 8 * 60; changed = true; } if (s.WorkDayEndMinutes <= s.WorkDayStartMinutes) { s.WorkDayEndMinutes = 17 * 60; changed = true; }
                if (s.BackupIntervalHours <= 0) { s.BackupIntervalHours = 4; changed = true; } if (s.LastBackupStatus == null) { s.LastBackupStatus = "لم تُنشأ نسخة بعد"; changed = true; } if (changed) settings.Update(s);
            }
            foreach (Patient p in patients.Find(x => x.NormalizedName == null || x.NormalizedName == "").ToList()) { p.NormalizedName = SaudiValidation.NormalizeArabicName(p.FullName); patients.Update(p); }
        }

        public AppSettings GetSettings() { return db.GetCollection<AppSettings>("settings").FindById(1); }
        public void SaveSettings(AppSettings settings)
        {
            if (settings.WorkDayStartMinutes < 0 || settings.WorkDayEndMinutes > 24 * 60 || settings.WorkDayStartMinutes >= settings.WorkDayEndMinutes) throw new InvalidOperationException("ساعات الدوام غير صحيحة.");
            if (settings.BackupIntervalHours < 1 || settings.BackupIntervalHours > 24) throw new InvalidOperationException("فترة النسخ الاحتياطي يجب أن تكون بين ساعة و24 ساعة.");
            settings.Id = 1; settings.UpdatedAt = DateTime.Now; db.GetCollection<AppSettings>("settings").Upsert(settings); Audit("تعديل الإعدادات", "Settings", "1", null, settings.ClinicName); db.Checkpoint();
        }

        public Patient AddPatient(Patient patient)
        {
            if (patient == null) throw new ArgumentNullException("patient"); if (CountAllPatients() >= MaxPatients) throw new InvalidOperationException("وصل النظام إلى الحد الإداري المحدد وهو 10,000 مراجع.");
            patient.NationalId = SaudiValidation.NormalizeDigits(patient.NationalId); patient.Mobile = SaudiValidation.NormalizeSaudiMobile(patient.Mobile); patient.NormalizedName = SaudiValidation.NormalizeArabicName(patient.FullName);
            if (FindByNationalId(patient.NationalId, true) != null) throw new InvalidOperationException("يوجد مراجع مسجل مسبقًا بنفس رقم الهوية/الإقامة.");
            Patient likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, null); if (likely != null) throw new DuplicatePatientException(likely);
            db.BeginTrans(); try
            {
                AppSettings settings = GetSettings(); patient.Id = Guid.NewGuid(); patient.FileNumber = settings.NextFileNumber; patient.CreatedAt = DateTime.Now; patient.UpdatedAt = patient.CreatedAt; patient.IsArchived = false;
                db.GetCollection<Patient>("patients").Insert(patient); settings.NextFileNumber++; settings.UpdatedAt = DateTime.Now; db.GetCollection<AppSettings>("settings").Update(settings); AuditInternal("إضافة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName); db.Commit(); db.Checkpoint(); return patient;
            }
            catch { db.Rollback(); throw; }
        }

        public void UpdatePatient(Patient patient)
        {
            patient.NationalId = SaudiValidation.NormalizeDigits(patient.NationalId); patient.Mobile = SaudiValidation.NormalizeSaudiMobile(patient.Mobile); patient.NormalizedName = SaudiValidation.NormalizeArabicName(patient.FullName);
            Patient sameId = FindByNationalId(patient.NationalId, true); if (sameId != null && sameId.Id != patient.Id) throw new InvalidOperationException("رقم الهوية/الإقامة مستخدم في ملف آخر رقم " + sameId.FileNumber + ".");
            Patient likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, patient.Id); if (likely != null) throw new DuplicatePatientException(likely);
            db.BeginTrans(); try { patient.UpdatedAt = DateTime.Now; if (!db.GetCollection<Patient>("patients").Update(patient)) throw new InvalidOperationException("تعذر العثور على ملف المراجع."); SyncPatientSnapshot(patient); AuditInternal("تعديل مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName); db.Commit(); db.Checkpoint(); } catch { db.Rollback(); throw; }
        }

        private void SyncPatientSnapshot(Patient patient)
        {
            var appointments = db.GetCollection<Appointment>("appointments"); foreach (Appointment a in appointments.Find(x => x.PatientId == patient.Id)) { a.PatientName = patient.FullName; a.FileNumber = patient.FileNumber; appointments.Update(a); }
            var tasks = db.GetCollection<PatientTask>("tasks"); foreach (PatientTask t in tasks.Find(x => x.PatientId == patient.Id)) { t.PatientName = patient.FullName; t.FileNumber = patient.FileNumber; tasks.Update(t); }
        }

        public void ArchivePatient(Guid id, string reason) { ArchivePatient(id, reason, true); }
        public void ArchivePatient(Guid id, string reason, bool closeFutureItems)
        {
            Patient patient = GetPatient(id); if (patient == null) return; db.BeginTrans(); try
            {
                patient.IsArchived = true; patient.ArchivedAt = DateTime.Now; patient.ArchiveReason = reason; patient.UpdatedAt = DateTime.Now; db.GetCollection<Patient>("patients").Update(patient);
                int closedAppointments = 0, closedTasks = 0;
                if (closeFutureItems)
                {
                    var ac = db.GetCollection<Appointment>("appointments"); foreach (Appointment a in ac.Find(x => x.PatientId == id && !x.IsDeleted && x.StartsAt >= DateTime.Now && x.Status != "ملغي").ToList()) { a.Status = "ملغي"; a.UpdatedAt = DateTime.Now; ac.Update(a); closedAppointments++; }
                    var tc = db.GetCollection<PatientTask>("tasks"); foreach (PatientTask t in tc.Find(x => x.PatientId == id && !x.IsDeleted && !x.IsCompleted).ToList()) { t.IsCompleted = true; t.UpdatedAt = DateTime.Now; tc.Update(t); closedTasks++; }
                }
                AuditInternal("أرشفة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, reason + "؛ أغلقت " + closedAppointments + " موعد و" + closedTasks + " مهمة"); db.Commit(); db.Checkpoint();
            }
            catch { db.Rollback(); throw; }
        }

        public void RestorePatient(Guid id) { Patient p = GetPatient(id); if (p == null) return; p.IsArchived = false; p.ArchivedAt = null; p.ArchiveReason = ""; p.UpdatedAt = DateTime.Now; db.GetCollection<Patient>("patients").Update(p); Audit("استعادة مراجع", "Patient", p.Id.ToString(), p.FileNumber, p.FullName); db.Checkpoint(); }
        public Patient GetPatient(Guid id) { return db.GetCollection<Patient>("patients").FindById(id); }
        public Patient FindByFileNumber(long number, bool includeArchived) { Patient p = db.GetCollection<Patient>("patients").FindOne(x => x.FileNumber == number); return p != null && (includeArchived || !p.IsArchived) ? p : null; }
        public Patient FindByNationalId(string id, bool includeArchived) { string n = SaudiValidation.NormalizeDigits(id); Patient p = db.GetCollection<Patient>("patients").FindOne(x => x.NationalId == n); return p != null && (includeArchived || !p.IsArchived) ? p : null; }
        private Patient FindLikelyDuplicate(string name, DateTime? birth, string mobile, Guid? exceptId)
        {
            string normalized = SaudiValidation.NormalizeArabicName(name), phone = SaudiValidation.NormalizeSaudiMobile(mobile);
            return db.GetCollection<Patient>("patients").Find(x => x.NormalizedName == normalized).FirstOrDefault(p => (!exceptId.HasValue || p.Id != exceptId.Value) && ((birth.HasValue && p.DateOfBirth.HasValue && birth.Value.Date == p.DateOfBirth.Value.Date) || (!string.IsNullOrEmpty(phone) && p.Mobile == phone)));
        }

        public List<Patient> SearchPatients(string mode, string term, bool includeArchived, string sort)
        {
            string q = (term ?? "").Trim(), digits = SaudiValidation.NormalizeDigits(q), name = SaudiValidation.NormalizeArabicName(q); var col = db.GetCollection<Patient>("patients"); IEnumerable<Patient> result;
            if (q.Length == 0) result = col.FindAll(); else if (mode == "رقم الملف") { long n; result = long.TryParse(digits, out n) ? col.Find(x => x.FileNumber == n) : Enumerable.Empty<Patient>(); }
            else if (mode == "الهوية/الإقامة") result = col.Find(x => x.NationalId == digits); else if (mode == "رقم الجوال") result = col.Find(x => x.Mobile == SaudiValidation.NormalizeSaudiMobile(q));
            else if (mode == "الاسم") result = col.Find(Query.Contains("NormalizedName", name)); else
            {
                var list = new Dictionary<Guid, Patient>(); long n; if (long.TryParse(digits, out n)) { Patient p = col.FindOne(x => x.FileNumber == n); if (p != null) list[p.Id] = p; p = col.FindOne(x => x.NationalId == digits); if (p != null) list[p.Id] = p; }
                foreach (Patient p in col.Find(Query.Contains("NormalizedName", name))) list[p.Id] = p; string phone = SaudiValidation.NormalizeSaudiMobile(q); foreach (Patient p in col.Find(x => x.Mobile == phone)) list[p.Id] = p; result = list.Values;
            }
            result = result.Where(p => includeArchived || !p.IsArchived); if (sort == "الاسم") result = result.OrderBy(p => p.FullName); else if (sort == "الأحدث") result = result.OrderByDescending(p => p.CreatedAt); else if (sort == "آخر مراجعة") result = result.OrderByDescending(p => p.LastVisitAt ?? DateTime.MinValue); else result = result.OrderBy(p => p.FileNumber);
            return result.Take(q.Length == 0 ? 1000 : MaxPatients).ToList();
        }

        public int CountActivePatients() { return db.GetCollection<Patient>("patients").Count(x => !x.IsArchived); }
        public int CountAllPatients() { return db.GetCollection<Patient>("patients").Count(); }
        public IEnumerable<Patient> GetAllPatients(bool includeArchived) { return db.GetCollection<Patient>("patients").FindAll().Where(p => includeArchived || !p.IsArchived).OrderBy(p => p.FileNumber); }

        public Appointment AddAppointment(Appointment a)
        {
            ValidateAppointmentAvailability(a); db.BeginTrans(); try { a.Id = Guid.NewGuid(); a.CreatedAt = DateTime.Now; a.UpdatedAt = a.CreatedAt; a.IsDeleted = false; db.GetCollection<Appointment>("appointments").Insert(a); RecalculateLastVisit(a.PatientId); AuditInternal("إضافة موعد", "Appointment", a.Id.ToString(), a.FileNumber, a.Title); db.Commit(); db.Checkpoint(); return a; } catch { db.Rollback(); throw; }
        }
        public void UpdateAppointment(Appointment a)
        {
            ValidateAppointmentAvailability(a); Appointment old = GetAppointment(a.Id); db.BeginTrans(); try { if (old != null && old.StartsAt != a.StartsAt) a.ReminderNotifiedAt = null; a.UpdatedAt = DateTime.Now; db.GetCollection<Appointment>("appointments").Update(a); RecalculateLastVisit(a.PatientId); if (old != null && old.PatientId != a.PatientId) RecalculateLastVisit(old.PatientId); AuditInternal("تعديل موعد", "Appointment", a.Id.ToString(), a.FileNumber, a.Title); db.Commit(); db.Checkpoint(); } catch { db.Rollback(); throw; }
        }
        public void DeleteAppointment(Guid id)
        {
            Appointment a = GetAppointment(id); if (a == null || a.IsDeleted) return; db.BeginTrans(); try { a.IsDeleted = true; a.DeletedAt = DateTime.Now; a.DeletedBy = currentUser; a.UpdatedAt = DateTime.Now; db.GetCollection<Appointment>("appointments").Update(a); RecalculateLastVisit(a.PatientId); AuditInternal("نقل موعد إلى المحذوفات", "Appointment", id.ToString(), a.FileNumber, a.Title); db.Commit(); db.Checkpoint(); } catch { db.Rollback(); throw; }
        }
        public void RestoreAppointment(Guid id) { Appointment a = GetAppointment(id); if (a == null || !a.IsDeleted) return; a.IsDeleted = false; a.DeletedAt = null; a.DeletedBy = ""; a.UpdatedAt = DateTime.Now; ValidateAppointmentAvailability(a); db.GetCollection<Appointment>("appointments").Update(a); RecalculateLastVisit(a.PatientId); Audit("استعادة موعد", "Appointment", id.ToString(), a.FileNumber, a.Title); db.Checkpoint(); }
        public Appointment GetAppointment(Guid id) { return db.GetCollection<Appointment>("appointments").FindById(id); }
        public void ValidateAppointmentAvailability(Appointment a)
        {
            if (a.IsDeleted) return; if (a.Id == Guid.Empty && a.StartsAt < DateTime.Now.AddMinutes(-1)) throw new InvalidOperationException("لا يمكن إنشاء موعد جديد في وقت سابق.");
            if (!SaudiValidation.IsOfficialWorkingDay(a.StartsAt)) throw new InvalidOperationException("لا يمكن حجز موعد يوم الجمعة أو السبت.");
            if (db.GetCollection<ClosureDate>("closures").Exists(x => x.Date == a.StartsAt.Date)) throw new InvalidOperationException("هذا اليوم مسجل كإجازة أو يوم إغلاق للمنشأة.");
            AppSettings s = GetSettings(); int start = a.StartsAt.Hour * 60 + a.StartsAt.Minute, endMinutes = start + a.DurationMinutes; if (start < s.WorkDayStartMinutes || endMinutes > s.WorkDayEndMinutes) throw new InvalidOperationException("الموعد خارج ساعات الدوام المحددة في الإعدادات.");
            DateTime end = a.StartsAt.AddMinutes(a.DurationMinutes), dayStart = a.StartsAt.Date, dayEnd = dayStart.AddDays(1);
            Appointment conflict = db.GetCollection<Appointment>("appointments").Find(x => x.StartsAt >= dayStart && x.StartsAt < dayEnd && !x.IsDeleted).FirstOrDefault(x => x.Id != a.Id && x.Status != "ملغي" && x.StartsAt < end && x.StartsAt.AddMinutes(x.DurationMinutes) > a.StartsAt);
            if (conflict != null) throw new AppointmentConflictException(conflict);
        }
        public List<Appointment> GetAppointments(DateTime? from, DateTime? to) { return GetAppointments(from, to, false); }
        public List<Appointment> GetAppointments(DateTime? from, DateTime? to, bool includeDeleted)
        {
            IEnumerable<Appointment> all = db.GetCollection<Appointment>("appointments").FindAll(); if (from.HasValue) all = all.Where(a => a.StartsAt >= from.Value); if (to.HasValue) all = all.Where(a => a.StartsAt < to.Value); if (!includeDeleted) all = all.Where(a => !a.IsDeleted); return all.OrderBy(a => a.StartsAt).ToList();
        }
        public List<Appointment> GetPatientAppointments(Guid patientId) { return db.GetCollection<Appointment>("appointments").Find(x => x.PatientId == patientId && !x.IsDeleted).OrderByDescending(x => x.StartsAt).ToList(); }
        public List<Appointment> GetDeletedAppointments() { return db.GetCollection<Appointment>("appointments").Find(x => x.IsDeleted).OrderByDescending(x => x.DeletedAt).ToList(); }
        public DateTime GetNextAvailableAppointmentTime(int durationMinutes)
        {
            AppSettings s = GetSettings(); DateTime now = DateTime.Now; int rounded = ((now.Minute + 14) / 15) * 15; DateTime candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddMinutes(rounded);
            if (candidate.Hour * 60 + candidate.Minute < s.WorkDayStartMinutes) candidate = candidate.Date.AddMinutes(s.WorkDayStartMinutes);
            for (int i = 0; i < 365 * 24 * 4; i++)
            {
                int minute = candidate.Hour * 60 + candidate.Minute; if (minute + durationMinutes > s.WorkDayEndMinutes) candidate = candidate.Date.AddDays(1).AddMinutes(s.WorkDayStartMinutes);
                if (!SaudiValidation.IsOfficialWorkingDay(candidate) || db.GetCollection<ClosureDate>("closures").Exists(x => x.Date == candidate.Date)) { candidate = candidate.Date.AddDays(1).AddMinutes(s.WorkDayStartMinutes); continue; }
                try { ValidateAppointmentAvailability(new Appointment { StartsAt = candidate, DurationMinutes = durationMinutes, Status = "مؤكد" }); return candidate; } catch (AppointmentConflictException) { candidate = candidate.AddMinutes(15); }
            }
            throw new InvalidOperationException("لم يتم العثور على وقت متاح خلال سنة.");
        }

        public PatientTask AddTask(PatientTask t) { t.Id = Guid.NewGuid(); t.CreatedAt = DateTime.Now; t.UpdatedAt = t.CreatedAt; t.IsDeleted = false; db.GetCollection<PatientTask>("tasks").Insert(t); Audit("إضافة مهمة", "Task", t.Id.ToString(), t.FileNumber, t.Title); db.Checkpoint(); return t; }
        public void UpdateTask(PatientTask t) { PatientTask old = db.GetCollection<PatientTask>("tasks").FindById(t.Id); if (old != null && old.DueAt != t.DueAt) t.ReminderNotifiedAt = null; t.UpdatedAt = DateTime.Now; db.GetCollection<PatientTask>("tasks").Update(t); Audit("تعديل مهمة", "Task", t.Id.ToString(), t.FileNumber, t.Title); db.Checkpoint(); }
        public void DeleteTask(Guid id) { PatientTask t = db.GetCollection<PatientTask>("tasks").FindById(id); if (t == null || t.IsDeleted) return; t.IsDeleted = true; t.DeletedAt = DateTime.Now; t.DeletedBy = currentUser; t.UpdatedAt = DateTime.Now; db.GetCollection<PatientTask>("tasks").Update(t); Audit("نقل مهمة إلى المحذوفات", "Task", id.ToString(), t.FileNumber, t.Title); db.Checkpoint(); }
        public void RestoreTask(Guid id) { PatientTask t = db.GetCollection<PatientTask>("tasks").FindById(id); if (t == null || !t.IsDeleted) return; t.IsDeleted = false; t.DeletedAt = null; t.DeletedBy = ""; t.UpdatedAt = DateTime.Now; db.GetCollection<PatientTask>("tasks").Update(t); Audit("استعادة مهمة", "Task", id.ToString(), t.FileNumber, t.Title); db.Checkpoint(); }
        public List<PatientTask> GetTasks(bool includeCompleted) { return db.GetCollection<PatientTask>("tasks").Find(x => !x.IsDeleted).Where(t => includeCompleted || !t.IsCompleted).OrderBy(t => t.DueAt).ToList(); }
        public List<PatientTask> GetPatientTasks(Guid id) { return db.GetCollection<PatientTask>("tasks").Find(x => x.PatientId == id && !x.IsDeleted).OrderByDescending(x => x.DueAt).ToList(); }
        public List<PatientTask> GetDeletedTasks() { return db.GetCollection<PatientTask>("tasks").Find(x => x.IsDeleted).OrderByDescending(x => x.DeletedAt).ToList(); }

        public Appointment GetNextUnnotifiedAppointment(DateTime from, DateTime to) { return db.GetCollection<Appointment>("appointments").Find(x => !x.IsDeleted && x.ReminderNotifiedAt == null && x.StartsAt >= from && x.StartsAt <= to && x.Status != "ملغي").OrderBy(x => x.StartsAt).FirstOrDefault(); }
        public PatientTask GetNextUnnotifiedTask(DateTime from, DateTime to) { return db.GetCollection<PatientTask>("tasks").Find(x => !x.IsDeleted && !x.IsCompleted && x.ReminderNotifiedAt == null && x.DueAt >= from && x.DueAt <= to).OrderBy(x => x.DueAt).FirstOrDefault(); }
        public void MarkAppointmentNotified(Guid id) { Appointment a = GetAppointment(id); if (a != null) { a.ReminderNotifiedAt = DateTime.Now; db.GetCollection<Appointment>("appointments").Update(a); db.Checkpoint(); } }
        public void MarkTaskNotified(Guid id) { PatientTask t = db.GetCollection<PatientTask>("tasks").FindById(id); if (t != null) { t.ReminderNotifiedAt = DateTime.Now; db.GetCollection<PatientTask>("tasks").Update(t); db.Checkpoint(); } }

        private void RecalculateLastVisit(Guid patientId)
        {
            Patient p = GetPatient(patientId); if (p == null) return; Appointment last = db.GetCollection<Appointment>("appointments").Find(x => x.PatientId == patientId && !x.IsDeleted && x.Status == "حضر" && x.StartsAt <= DateTime.Now).OrderByDescending(x => x.StartsAt).FirstOrDefault(); p.LastVisitAt = last == null ? (DateTime?)null : last.StartsAt; p.UpdatedAt = DateTime.Now; db.GetCollection<Patient>("patients").Update(p);
        }
        public List<Patient> GetInventoryCandidates(DateTime asOf)
        {
            DateTime cutoff = asOf.Date.AddYears(-10); var attended = db.GetCollection<Appointment>("appointments").Find(x => !x.IsDeleted && x.Status == "حضر" && x.StartsAt <= asOf).GroupBy(x => x.PatientId).ToDictionary(g => g.Key, g => g.Max(x => x.StartsAt));
            return db.GetCollection<Patient>("patients").Find(x => !x.IsArchived).Where(p => { DateTime last = p.CreatedAt; DateTime a; if (attended.TryGetValue(p.Id, out a) && a > last) last = a; return last <= cutoff; }).OrderBy(p => p.FileNumber).ToList();
        }
        public void SetInventoryAlerted(int year) { AppSettings s = GetSettings(); s.LastInventoryAlertYear = year; SaveSettings(s); }

        public List<ClosureDate> GetClosures() { return db.GetCollection<ClosureDate>("closures").FindAll().OrderBy(x => x.Date).ToList(); }
        public void AddClosure(DateTime date, string reason) { if (!SaudiValidation.IsOfficialWorkingDay(date)) throw new InvalidOperationException("الجمعة والسبت مستبعدان أصلًا من المواعيد."); if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("أدخل سبب الإغلاق أو اسم الإجازة."); var c = new ClosureDate { Id = Guid.NewGuid(), Date = date.Date, Reason = reason.Trim(), CreatedAt = DateTime.Now }; db.GetCollection<ClosureDate>("closures").Insert(c); Audit("إضافة يوم إغلاق", "Closure", c.Id.ToString(), null, c.Date.ToString("yyyy-MM-dd") + " " + c.Reason); db.Checkpoint(); }
        public void DeleteClosure(Guid id) { ClosureDate c = db.GetCollection<ClosureDate>("closures").FindById(id); if (c == null) return; db.GetCollection<ClosureDate>("closures").Delete(id); Audit("حذف يوم إغلاق", "Closure", id.ToString(), null, c.Date.ToString("yyyy-MM-dd") + " " + c.Reason); db.Checkpoint(); }

        public void Audit(string action, string entityType, string entityId, long? fileNumber, string details) { AuditInternal(action, entityType, entityId, fileNumber, details); }
        private void AuditInternal(string action, string entityType, string entityId, long? fileNumber, string details) { db.GetCollection<AuditEntry>("audit").Insert(new AuditEntry { Id = Guid.NewGuid(), OccurredAt = DateTime.Now, Action = action, EntityType = entityType, EntityId = entityId, FileNumber = fileNumber, Details = details ?? "", MachineName = Environment.MachineName, UserName = currentUser }); }
        public List<AuditEntry> GetRecentAudit(int count) { return db.GetCollection<AuditEntry>("audit").FindAll().OrderByDescending(x => x.OccurredAt).Take(count).ToList(); }
        public void Checkpoint() { if (db != null) db.Checkpoint(); }
        public void ValidateDatabaseFile(string path)
        {
            using (var test = new LiteDatabase(new ConnectionString { Filename = path, Password = databasePassword, Connection = ConnectionType.Direct, ReadOnly = true }))
                if (test.GetCollection<AppSettings>("settings").FindById(1) == null) throw new InvalidDataException("قاعدة بيانات النسخة لا تحتوي إعدادات النظام.");
        }
        public void Close() { if (db != null) { db.Checkpoint(); db.Dispose(); db = null; } }
        public void Reopen() { if (db == null) Open(); }
        public void Dispose() { Close(); }
    }

    public sealed class DuplicatePatientException : Exception { public Patient ExistingPatient { get; private set; } public DuplicatePatientException(Patient p) : base("قد يكون المراجع مسجلًا مسبقًا في الملف رقم " + p.FileNumber + ".") { ExistingPatient = p; } }
    public sealed class AppointmentConflictException : Exception { public Appointment ExistingAppointment { get; private set; } public AppointmentConflictException(Appointment a) : base("يوجد موعد متعارض للمراجع " + a.PatientName + "، ملف " + a.FileNumber + ".") { ExistingAppointment = a; } }
}

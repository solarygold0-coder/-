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
        private LiteDatabase db;
        private readonly string databasePassword;
        public string DataDirectory { get; private set; }
        public string DatabasePath { get; private set; }

        public AppDatabase(string dataDirectory, string password)
        {
            DataDirectory = dataDirectory;
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "patients.db");
            databasePassword = password;
            Open();
        }

        private void Open()
        {
            var connection = new ConnectionString
            {
                Filename = DatabasePath,
                Password = databasePassword,
                Connection = ConnectionType.Shared,
                Upgrade = false
            };
            db = new LiteDatabase(connection);
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            var patients = db.GetCollection<Patient>("patients");
            patients.EnsureIndex(x => x.FileNumber, true);
            patients.EnsureIndex(x => x.NationalId, true);
            patients.EnsureIndex(x => x.FullName);
            patients.EnsureIndex(x => x.Mobile);
            patients.EnsureIndex(x => x.IsArchived);

            var appointments = db.GetCollection<Appointment>("appointments");
            appointments.EnsureIndex(x => x.PatientId);
            appointments.EnsureIndex(x => x.FileNumber);
            appointments.EnsureIndex(x => x.StartsAt);

            var tasks = db.GetCollection<PatientTask>("tasks");
            tasks.EnsureIndex(x => x.PatientId);
            tasks.EnsureIndex(x => x.FileNumber);
            tasks.EnsureIndex(x => x.DueAt);

            var settings = db.GetCollection<AppSettings>("settings");
            if (settings.FindById(1) == null)
            {
                settings.Insert(new AppSettings
                {
                    Id = 1,
                    NextFileNumber = 1,
                    ClinicName = "المنشأة الصحية",
                    DefaultAppointmentMinutes = 30,
                    UpdatedAt = DateTime.Now
                });
            }
        }

        public AppSettings GetSettings() { return db.GetCollection<AppSettings>("settings").FindById(1); }
        public void SaveSettings(AppSettings settings)
        {
            settings.Id = 1;
            settings.UpdatedAt = DateTime.Now;
            db.GetCollection<AppSettings>("settings").Upsert(settings);
            Audit("تعديل الإعدادات", "Settings", "1", null, settings.ClinicName);
            db.Checkpoint();
        }

        public Patient AddPatient(Patient patient)
        {
            if (patient == null) throw new ArgumentNullException("patient");
            if (FindByNationalId(patient.NationalId, true) != null)
                throw new InvalidOperationException("يوجد مراجع مسجل مسبقًا بنفس رقم الهوية/الإقامة.");
            Patient likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, null);
            if (likely != null)
                throw new DuplicatePatientException(likely);

            db.BeginTrans();
            try
            {
                var settings = GetSettings();
                patient.Id = Guid.NewGuid();
                patient.FileNumber = settings.NextFileNumber;
                patient.CreatedAt = DateTime.Now;
                patient.UpdatedAt = patient.CreatedAt;
                patient.IsArchived = false;
                db.GetCollection<Patient>("patients").Insert(patient);
                settings.NextFileNumber++;
                settings.UpdatedAt = DateTime.Now;
                db.GetCollection<AppSettings>("settings").Update(settings);
                AuditInternal("إضافة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName);
                db.Commit();
                db.Checkpoint();
                return patient;
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        public void UpdatePatient(Patient patient)
        {
            Patient sameId = FindByNationalId(patient.NationalId, true);
            if (sameId != null && sameId.Id != patient.Id)
                throw new InvalidOperationException("رقم الهوية/الإقامة مستخدم في ملف آخر رقم " + sameId.FileNumber + ".");
            Patient likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, patient.Id);
            if (likely != null) throw new DuplicatePatientException(likely);
            patient.UpdatedAt = DateTime.Now;
            if (!db.GetCollection<Patient>("patients").Update(patient))
                throw new InvalidOperationException("تعذر العثور على ملف المراجع.");
            SyncPatientSnapshot(patient);
            Audit("تعديل مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName);
            db.Checkpoint();
        }

        private void SyncPatientSnapshot(Patient patient)
        {
            var appointments = db.GetCollection<Appointment>("appointments");
            foreach (Appointment a in appointments.Find(x => x.PatientId == patient.Id))
            {
                a.PatientName = patient.FullName;
                a.FileNumber = patient.FileNumber;
                appointments.Update(a);
            }
            var tasks = db.GetCollection<PatientTask>("tasks");
            foreach (PatientTask t in tasks.Find(x => x.PatientId == patient.Id))
            {
                t.PatientName = patient.FullName;
                t.FileNumber = patient.FileNumber;
                tasks.Update(t);
            }
        }

        public void ArchivePatient(Guid id, string reason)
        {
            Patient patient = GetPatient(id);
            if (patient == null) return;
            patient.IsArchived = true;
            patient.ArchivedAt = DateTime.Now;
            patient.ArchiveReason = reason;
            patient.UpdatedAt = DateTime.Now;
            db.GetCollection<Patient>("patients").Update(patient);
            Audit("أرشفة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, reason);
            db.Checkpoint();
        }

        public void RestorePatient(Guid id)
        {
            Patient patient = GetPatient(id);
            if (patient == null) return;
            patient.IsArchived = false;
            patient.ArchivedAt = null;
            patient.ArchiveReason = "";
            patient.UpdatedAt = DateTime.Now;
            db.GetCollection<Patient>("patients").Update(patient);
            Audit("استعادة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName);
            db.Checkpoint();
        }

        public Patient GetPatient(Guid id) { return db.GetCollection<Patient>("patients").FindById(id); }
        public Patient FindByFileNumber(long number, bool includeArchived)
        {
            Patient p = db.GetCollection<Patient>("patients").FindOne(x => x.FileNumber == number);
            return p != null && (includeArchived || !p.IsArchived) ? p : null;
        }
        public Patient FindByNationalId(string nationalId, bool includeArchived)
        {
            string normalized = SaudiValidation.NormalizeDigits(nationalId);
            Patient p = db.GetCollection<Patient>("patients").FindOne(x => x.NationalId == normalized);
            return p != null && (includeArchived || !p.IsArchived) ? p : null;
        }

        private Patient FindLikelyDuplicate(string name, DateTime? birth, string mobile, Guid? exceptId)
        {
            string cleanName = (name ?? "").Trim();
            string cleanMobile = SaudiValidation.NormalizeSaudiMobile(mobile);
            IEnumerable<Patient> candidates = db.GetCollection<Patient>("patients").Find(Query.EQ("FullName", cleanName));
            return candidates.FirstOrDefault(p => (!exceptId.HasValue || p.Id != exceptId.Value) &&
                ((birth.HasValue && p.DateOfBirth.HasValue && birth.Value.Date == p.DateOfBirth.Value.Date) ||
                 (!string.IsNullOrEmpty(cleanMobile) && p.Mobile == cleanMobile)));
        }

        public List<Patient> SearchPatients(string mode, string term, bool includeArchived, string sort)
        {
            string q = (term ?? "").Trim();
            string digits = SaudiValidation.NormalizeDigits(q);
            var col = db.GetCollection<Patient>("patients");
            IEnumerable<Patient> result;
            if (string.IsNullOrEmpty(q)) result = col.FindAll();
            else if (mode == "رقم الملف")
            {
                long n;
                result = long.TryParse(digits, out n) ? col.Find(x => x.FileNumber == n) : Enumerable.Empty<Patient>();
            }
            else if (mode == "الهوية/الإقامة") result = col.Find(x => x.NationalId == digits);
            else if (mode == "رقم الجوال") result = col.Find(x => x.Mobile == SaudiValidation.NormalizeSaudiMobile(q));
            else if (mode == "الاسم") result = col.Find(Query.Contains("FullName", q));
            else
            {
                var list = new Dictionary<Guid, Patient>();
                long n;
                if (long.TryParse(digits, out n))
                {
                    Patient p = col.FindOne(x => x.FileNumber == n);
                    if (p != null) list[p.Id] = p;
                    p = col.FindOne(x => x.NationalId == digits);
                    if (p != null) list[p.Id] = p;
                }
                foreach (Patient p in col.Find(Query.Contains("FullName", q))) list[p.Id] = p;
                string phone = SaudiValidation.NormalizeSaudiMobile(q);
                foreach (Patient p in col.Find(x => x.Mobile == phone)) list[p.Id] = p;
                result = list.Values;
            }
            result = result.Where(p => includeArchived || !p.IsArchived);
            if (sort == "الاسم") result = result.OrderBy(p => p.FullName);
            else if (sort == "الأحدث") result = result.OrderByDescending(p => p.CreatedAt);
            else if (sort == "آخر مراجعة") result = result.OrderByDescending(p => p.LastVisitAt ?? DateTime.MinValue);
            else result = result.OrderBy(p => p.FileNumber);
            // Keep an unfiltered screen responsive with very large registries; exact/name searches still cover the full database.
            int displayLimit = string.IsNullOrEmpty(q) ? 2000 : 100000;
            return result.Take(displayLimit).ToList();
        }

        public int CountActivePatients() { return db.GetCollection<Patient>("patients").Find(x => !x.IsArchived).Count(); }
        public IEnumerable<Patient> GetAllPatients(bool includeArchived)
        {
            return db.GetCollection<Patient>("patients").FindAll().Where(p => includeArchived || !p.IsArchived).OrderBy(p => p.FileNumber);
        }

        public Appointment AddAppointment(Appointment appointment)
        {
            ValidateAppointmentAvailability(appointment);
            appointment.Id = Guid.NewGuid();
            appointment.CreatedAt = DateTime.Now;
            appointment.UpdatedAt = appointment.CreatedAt;
            db.GetCollection<Appointment>("appointments").Insert(appointment);
            UpdateLastVisitFromAppointment(appointment);
            Audit("إضافة موعد", "Appointment", appointment.Id.ToString(), appointment.FileNumber, appointment.Title);
            db.Checkpoint();
            return appointment;
        }
        public void UpdateAppointment(Appointment appointment)
        {
            ValidateAppointmentAvailability(appointment);
            appointment.UpdatedAt = DateTime.Now;
            db.GetCollection<Appointment>("appointments").Update(appointment);
            UpdateLastVisitFromAppointment(appointment);
            Audit("تعديل موعد", "Appointment", appointment.Id.ToString(), appointment.FileNumber, appointment.Title);
            db.Checkpoint();
        }
        public void DeleteAppointment(Guid id)
        {
            Appointment a = db.GetCollection<Appointment>("appointments").FindById(id);
            if (a == null) return;
            db.GetCollection<Appointment>("appointments").Delete(id);
            Audit("حذف موعد", "Appointment", id.ToString(), a.FileNumber, a.Title);
            db.Checkpoint();
        }
        public void ValidateAppointmentAvailability(Appointment a)
        {
            if (!SaudiValidation.IsOfficialWorkingDay(a.StartsAt))
                throw new InvalidOperationException("لا يمكن حجز موعد يوم الجمعة أو السبت وفق أيام الدوام المحددة.");
            DateTime end = a.StartsAt.AddMinutes(a.DurationMinutes);
            DateTime dayStart = a.StartsAt.Date;
            DateTime dayEnd = dayStart.AddDays(1);
            Appointment conflict = db.GetCollection<Appointment>("appointments")
                .Find(x => x.StartsAt >= dayStart && x.StartsAt < dayEnd)
                .FirstOrDefault(x => x.Id != a.Id && x.Status != "ملغي" && x.StartsAt < end && x.StartsAt.AddMinutes(x.DurationMinutes) > a.StartsAt);
            if (conflict != null)
                throw new AppointmentConflictException(conflict);
        }
        public Appointment GetAppointment(Guid id) { return db.GetCollection<Appointment>("appointments").FindById(id); }
        private void UpdateLastVisitFromAppointment(Appointment appointment)
        {
            if (appointment.Status != "حضر" || appointment.StartsAt > DateTime.Now) return;
            Patient patient = GetPatient(appointment.PatientId);
            if (patient == null) return;
            if (!patient.LastVisitAt.HasValue || appointment.StartsAt > patient.LastVisitAt.Value)
            {
                patient.LastVisitAt = appointment.StartsAt;
                patient.UpdatedAt = DateTime.Now;
                db.GetCollection<Patient>("patients").Update(patient);
            }
        }
        public List<Appointment> GetAppointments(DateTime? from, DateTime? to)
        {
            IEnumerable<Appointment> all = db.GetCollection<Appointment>("appointments").FindAll();
            if (from.HasValue) all = all.Where(a => a.StartsAt >= from.Value);
            if (to.HasValue) all = all.Where(a => a.StartsAt < to.Value);
            return all.OrderBy(a => a.StartsAt).ToList();
        }

        public PatientTask AddTask(PatientTask task)
        {
            task.Id = Guid.NewGuid(); task.CreatedAt = DateTime.Now; task.UpdatedAt = task.CreatedAt;
            db.GetCollection<PatientTask>("tasks").Insert(task);
            Audit("إضافة مهمة", "Task", task.Id.ToString(), task.FileNumber, task.Title);
            db.Checkpoint(); return task;
        }
        public void UpdateTask(PatientTask task)
        {
            task.UpdatedAt = DateTime.Now; db.GetCollection<PatientTask>("tasks").Update(task);
            Audit("تعديل مهمة", "Task", task.Id.ToString(), task.FileNumber, task.Title); db.Checkpoint();
        }
        public void DeleteTask(Guid id)
        {
            PatientTask t = db.GetCollection<PatientTask>("tasks").FindById(id); if (t == null) return;
            db.GetCollection<PatientTask>("tasks").Delete(id);
            Audit("حذف مهمة", "Task", id.ToString(), t.FileNumber, t.Title); db.Checkpoint();
        }
        public List<PatientTask> GetTasks(bool includeCompleted)
        {
            return db.GetCollection<PatientTask>("tasks").FindAll()
                .Where(t => includeCompleted || !t.IsCompleted).OrderBy(t => t.DueAt).ToList();
        }

        public List<Patient> GetInventoryCandidates(DateTime asOf)
        {
            DateTime cutoff = asOf.Date.AddYears(-10);
            var lastAppointments = db.GetCollection<Appointment>("appointments").Find(x => x.Status == "حضر")
                .GroupBy(a => a.PatientId).ToDictionary(g => g.Key, g => g.Max(a => a.StartsAt));
            return db.GetCollection<Patient>("patients").Find(x => !x.IsArchived)
                .Where(p =>
                {
                    DateTime last = p.LastVisitAt ?? p.CreatedAt;
                    DateTime appt;
                    if (lastAppointments.TryGetValue(p.Id, out appt) && appt > last) last = appt;
                    return last <= cutoff;
                }).OrderBy(p => p.FileNumber).ToList();
        }

        public void SetInventoryAlerted(int year)
        {
            AppSettings s = GetSettings(); s.LastInventoryAlertYear = year; SaveSettings(s);
        }

        public void Audit(string action, string entityType, string entityId, long? fileNumber, string details)
        {
            AuditInternal(action, entityType, entityId, fileNumber, details);
        }
        private void AuditInternal(string action, string entityType, string entityId, long? fileNumber, string details)
        {
            db.GetCollection<AuditEntry>("audit").Insert(new AuditEntry
            {
                Id = Guid.NewGuid(), OccurredAt = DateTime.Now, Action = action, EntityType = entityType,
                EntityId = entityId, FileNumber = fileNumber, Details = details ?? "", MachineName = Environment.MachineName
            });
        }

        public void Checkpoint() { if (db != null) db.Checkpoint(); }
        public void Close() { if (db != null) { db.Checkpoint(); db.Dispose(); db = null; } }
        public void Reopen() { if (db == null) Open(); }
        public void Dispose() { Close(); }
    }

    public sealed class DuplicatePatientException : Exception
    {
        public Patient ExistingPatient { get; private set; }
        public DuplicatePatientException(Patient existing) : base("قد يكون المراجع مسجلًا مسبقًا في الملف رقم " + existing.FileNumber + ".") { ExistingPatient = existing; }
    }
    public sealed class AppointmentConflictException : Exception
    {
        public Appointment ExistingAppointment { get; private set; }
        public AppointmentConflictException(Appointment existing) : base("يوجد موعد متعارض في نفس الوقت للمراجع " + existing.PatientName + "، ملف " + existing.FileNumber + ".") { ExistingAppointment = existing; }
    }
}

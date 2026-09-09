using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PatientRecordsSaudi.Models;

namespace PatientRecordsSaudi.Services
{
    public sealed partial class AppDatabase : IDisposable
    {
        public const int MaxPatients = 10000;
        public const int DefaultPatientListLimit = 500;
        public const int MaxAuditEntries = 50000;
        public const long MaxAttachmentBytes = 10L * 1024L * 1024L;
        private const int SchemaVersion = 1;
        private static readonly string[] AllowedAttachmentExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".docx" };
        private const string TemporaryAttachmentPrefix = "SaudiPatientRecordsView_";
        private static readonly string TemporaryAttachmentDirectory = Path.Combine(Path.GetTempPath(), TemporaryAttachmentPrefix + Environment.ProcessId.ToString("X") + "_" + Guid.NewGuid().ToString("N"));
        private static readonly object NativeInitLock = new object();
        private static bool nativeInitialized;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IgnoreReadOnlyProperties = true };
        private SqliteConnection? db;
        private SqliteTransaction? transaction;
        private byte[]? databasePasswordBytes;
        private string currentUser, currentRole;

        public string DataDirectory { get; private set; }
        public string DatabasePath { get; private set; }

        public AppDatabase(string dataDirectory, string password) : this(dataDirectory, password, "النظام", "مدير") { }
        public AppDatabase(string dataDirectory, string password, string user) : this(dataDirectory, password, user, "مدير") { }
        public AppDatabase(string dataDirectory, string password, string user, string role)
        {
            DataDirectory = dataDirectory;
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "patients.sqlite3");
            databasePasswordBytes = Encoding.UTF8.GetBytes(password ?? "");
            currentUser = string.IsNullOrWhiteSpace(user) ? "النظام" : user.Trim();
            currentRole = string.IsNullOrWhiteSpace(role) ? "قراءة فقط" : role;
            Open();
        }

        public void SetCurrentUser(string user) { currentUser = string.IsNullOrWhiteSpace(user) ? "النظام" : user.Trim(); }
        public void SetCurrentSession(string user, string role) { SetCurrentUser(user); currentRole = string.IsNullOrWhiteSpace(role) ? "قراءة فقط" : role; }
        private void RequireWrite() { if (currentRole == "قراءة فقط") throw new UnauthorizedAccessException("الحساب بصلاحية قراءة فقط ولا يملك تعديل البيانات."); }
        private void RequireAdmin() { if (currentRole != "مدير" && currentRole != "مالك محلي") throw new UnauthorizedAccessException("هذه العملية متاحة للمدير فقط."); }

        private static void EnsureNativeInitialized()
        {
            lock (NativeInitLock)
            {
                if (nativeInitialized) return;
                SQLitePCL.Batteries_V2.Init();
                nativeInitialized = true;
            }
        }

        private string ConnectionString(string path, SqliteOpenMode mode)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                ForeignKeys = true,
                DefaultTimeout = 15,
                Password = MaterializeDatabasePassword()
            }.ToString();
        }

        private void Open()
        {
            EnsureNativeInitialized();
            db = new SqliteConnection(ConnectionString(DatabasePath, SqliteOpenMode.ReadWriteCreate));
            db.Open();
            Execute("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000; PRAGMA secure_delete=ON; PRAGMA synchronous=FULL;");
            Execute("PRAGMA journal_mode=WAL;");
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            Execute(@"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS patients (
 id TEXT PRIMARY KEY, file_number INTEGER NOT NULL UNIQUE, national_id TEXT NOT NULL UNIQUE,
 normalized_name TEXT NOT NULL, mobile TEXT NOT NULL, city TEXT NOT NULL,
 date_of_birth_ticks INTEGER NULL, created_ticks INTEGER NOT NULL, last_visit_ticks INTEGER NULL,
 is_archived INTEGER NOT NULL CHECK(is_archived IN (0,1)), payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_patients_name ON patients(normalized_name);
CREATE INDEX IF NOT EXISTS ix_patients_mobile ON patients(mobile);
CREATE INDEX IF NOT EXISTS ix_patients_city ON patients(city);
CREATE INDEX IF NOT EXISTS ix_patients_birth ON patients(date_of_birth_ticks);
CREATE INDEX IF NOT EXISTS ix_patients_archive ON patients(is_archived);
CREATE TABLE IF NOT EXISTS appointments (
 id TEXT PRIMARY KEY, patient_id TEXT NOT NULL, file_number INTEGER NOT NULL,
 starts_ticks INTEGER NOT NULL, duration_minutes INTEGER NOT NULL, status TEXT NOT NULL,
 is_deleted INTEGER NOT NULL CHECK(is_deleted IN (0,1)), reminder_ticks INTEGER NULL,
 deleted_ticks INTEGER NULL, payload TEXT NOT NULL,
 FOREIGN KEY(patient_id) REFERENCES patients(id));
CREATE INDEX IF NOT EXISTS ix_appointments_patient ON appointments(patient_id);
CREATE INDEX IF NOT EXISTS ix_appointments_start ON appointments(starts_ticks);
CREATE INDEX IF NOT EXISTS ix_appointments_deleted ON appointments(is_deleted);
CREATE TABLE IF NOT EXISTS tasks (
 id TEXT PRIMARY KEY, patient_id TEXT NOT NULL, file_number INTEGER NOT NULL,
 due_ticks INTEGER NOT NULL, is_completed INTEGER NOT NULL CHECK(is_completed IN (0,1)),
 is_deleted INTEGER NOT NULL CHECK(is_deleted IN (0,1)), reminder_ticks INTEGER NULL,
 deleted_ticks INTEGER NULL, payload TEXT NOT NULL,
 FOREIGN KEY(patient_id) REFERENCES patients(id));
CREATE INDEX IF NOT EXISTS ix_tasks_patient ON tasks(patient_id);
CREATE INDEX IF NOT EXISTS ix_tasks_due ON tasks(due_ticks);
CREATE INDEX IF NOT EXISTS ix_tasks_deleted ON tasks(is_deleted);
CREATE TABLE IF NOT EXISTS attachments (
 id TEXT PRIMARY KEY, patient_id TEXT NOT NULL, file_number INTEGER NOT NULL,
 is_deleted INTEGER NOT NULL CHECK(is_deleted IN (0,1)), deleted_ticks INTEGER NULL,
 payload TEXT NOT NULL, content BLOB NOT NULL,
 FOREIGN KEY(patient_id) REFERENCES patients(id));
CREATE INDEX IF NOT EXISTS ix_attachments_patient ON attachments(patient_id);
CREATE INDEX IF NOT EXISTS ix_attachments_deleted ON attachments(is_deleted);
CREATE TABLE IF NOT EXISTS closures (
 id TEXT PRIMARY KEY, date_ticks INTEGER NOT NULL UNIQUE, payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS audit (
 id TEXT PRIMARY KEY, occurred_ticks INTEGER NOT NULL, payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_audit_time ON audit(occurred_ticks DESC);
CREATE TABLE IF NOT EXISTS assets (key TEXT PRIMARY KEY, content BLOB NOT NULL);
INSERT INTO meta(key,value) VALUES('schema_version',$version)
 ON CONFLICT(key) DO NOTHING;", ("$version", SchemaVersion));

            string version = ScalarString("SELECT value FROM meta WHERE key='schema_version';");
            if (!int.TryParse(version, out int parsed) || parsed != SchemaVersion) throw new InvalidDataException("إصدار مخطط قاعدة SQLite غير مدعوم.");
            AppSettings? settings = QueryPayload<AppSettings>("SELECT payload FROM settings WHERE id=1;").FirstOrDefault();
            if (settings == null) UpsertSettingsInternal(DefaultSettings());
            else if (EnsureSettingsDefaults(settings)) UpsertSettingsInternal(settings);
            long highest = ScalarLong("SELECT COALESCE(MAX(file_number),0) FROM patients;");
            settings = GetSettings();
            if (settings.NextFileNumber <= highest) { settings.NextFileNumber = highest + 1; settings.UpdatedAt = DateTime.Now; UpsertSettingsInternal(settings); }
            Execute("UPDATE patients SET normalized_name='' WHERE normalized_name IS NULL;");
            TrimAuditIfNeeded();
        }

        public AppSettings GetSettings()
        {
            AppSettings? value = QueryPayload<AppSettings>("SELECT payload FROM settings WHERE id=1;").FirstOrDefault();
            if (value == null) { value = DefaultSettings(); UpsertSettingsInternal(value); }
            else if (EnsureSettingsDefaults(value)) UpsertSettingsInternal(value);
            return value;
        }

        public void SaveSettings(AppSettings settings)
        {
            RequireAdmin();
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.WorkDayStartMinutes < 0 || settings.WorkDayEndMinutes > 24 * 60 || settings.WorkDayStartMinutes >= settings.WorkDayEndMinutes) throw new InvalidOperationException("ساعات الدوام غير صحيحة.");
            if (settings.DefaultAppointmentMinutes < 5 || settings.DefaultAppointmentMinutes > 12 * 60) throw new InvalidOperationException("مدة الموعد الافتراضية غير صحيحة.");
            if (settings.BackupIntervalHours < 1 || settings.BackupIntervalHours > 24) throw new InvalidOperationException("فترة النسخ الاحتياطي يجب أن تكون بين ساعة و24 ساعة.");
            settings.AutoBackupDirectory = (settings.AutoBackupDirectory ?? "").Trim();
            NormalizeLookups(settings); settings.Id = 1; settings.UpdatedAt = DateTime.Now;
            InTransaction(() => { UpsertSettingsInternal(settings); AuditInternal("تعديل الإعدادات", "Settings", "1", null, settings.ClinicName); });
            Checkpoint();
        }

        private void UpsertSettingsInternal(AppSettings settings)
        {
            Execute("INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload;", ("$payload", Serialize(settings)));
        }

        public void SetClinicLogo(string sourcePath)
        {
            RequireAdmin(); var info = new FileInfo(sourcePath);
            if (!info.Exists) throw new FileNotFoundException("ملف الشعار غير موجود.");
            if (info.Length > 5L * 1024L * 1024L) throw new InvalidOperationException("حجم الشعار يجب ألا يتجاوز 5 ميجابايت.");
            byte[] content = File.ReadAllBytes(sourcePath);
            InTransaction(() =>
            {
                Execute("INSERT INTO assets(key,content) VALUES('clinic_logo',$content) ON CONFLICT(key) DO UPDATE SET content=excluded.content;", ("$content", content));
                AppSettings s = GetSettings(); s.ClinicLogoStoredId = "clinic_logo"; s.ClinicLogoFileName = info.Name; s.UpdatedAt = DateTime.Now; UpsertSettingsInternal(s);
                AuditInternal("تحديث شعار المنشأة", "Settings", "1", null, info.Name);
            });
            Checkpoint();
        }

        public byte[]? GetClinicLogo()
        {
            AppSettings settings = GetSettings(); if (string.IsNullOrWhiteSpace(settings.ClinicLogoStoredId)) return null;
            using (SqliteCommand cmd = Command("SELECT content FROM assets WHERE key='clinic_logo';"))
            { object? value = cmd.ExecuteScalar(); return value == null || value == DBNull.Value ? null : (byte[])value; }
        }

        public void RemoveClinicLogo()
        {
            RequireAdmin(); InTransaction(() => { Execute("DELETE FROM assets WHERE key='clinic_logo';"); AppSettings s = GetSettings(); s.ClinicLogoStoredId = ""; s.ClinicLogoFileName = ""; s.UpdatedAt = DateTime.Now; UpsertSettingsInternal(s); AuditInternal("إزالة شعار المنشأة", "Settings", "1", null, ""); }); Checkpoint();
        }

        private static AppSettings DefaultSettings()
        {
            var s = new AppSettings { Id = 1, NextFileNumber = 1, ClinicName = "المنشأة", ClinicPhone = "", ClinicAddress = "", ClinicLogoStoredId = "", ClinicLogoFileName = "", DefaultAppointmentMinutes = 30, WorkDayStartMinutes = 8 * 60, WorkDayEndMinutes = 17 * 60, BackupIntervalHours = 4, AutoBackupDirectory = "", LastBackupStatus = "لم تُنشأ نسخة بعد", UpdatedAt = DateTime.Now };
            EnsureLookups(s); return s;
        }

        private static bool EnsureSettingsDefaults(AppSettings s)
        {
            bool changed = false;
            if (s.NextFileNumber < 1) { s.NextFileNumber = 1; changed = true; }
            if (string.IsNullOrWhiteSpace(s.ClinicName)) { s.ClinicName = "المنشأة"; changed = true; }
            if (s.ClinicPhone == null) { s.ClinicPhone = ""; changed = true; }
            if (s.ClinicAddress == null) { s.ClinicAddress = ""; changed = true; }
            if (s.ClinicLogoStoredId == null) { s.ClinicLogoStoredId = ""; changed = true; }
            if (s.ClinicLogoFileName == null) { s.ClinicLogoFileName = ""; changed = true; }
            if (s.AutoBackupDirectory == null) { s.AutoBackupDirectory = ""; changed = true; }
            if (s.DefaultAppointmentMinutes <= 0) { s.DefaultAppointmentMinutes = 30; changed = true; }
            if (s.WorkDayStartMinutes <= 0) { s.WorkDayStartMinutes = 8 * 60; changed = true; }
            if (s.WorkDayEndMinutes <= s.WorkDayStartMinutes) { s.WorkDayEndMinutes = 17 * 60; changed = true; }
            if (s.BackupIntervalHours <= 0) { s.BackupIntervalHours = 4; changed = true; }
            if (s.LastBackupStatus == null) { s.LastBackupStatus = "لم تُنشأ نسخة بعد"; changed = true; }
            changed |= EnsureLookups(s); return changed;
        }

        private static bool EnsureLookups(AppSettings s)
        {
            bool changed = false;
            if (s.VisitTypes == null || s.VisitTypes.Count == 0) { s.VisitTypes = new List<string> { "مراجعة", "موعد جديد", "إجراء", "نتائج", "استشارة" }; changed = true; }
            if (s.AppointmentStatuses == null || s.AppointmentStatuses.Count == 0) { s.AppointmentStatuses = new List<string> { "مؤكد", "بانتظار التأكيد", "حضر", "لم يحضر", "ملغي" }; changed = true; }
            if (s.TaskPriorities == null || s.TaskPriorities.Count == 0) { s.TaskPriorities = new List<string> { "عادية", "مرتفعة", "عاجلة" }; changed = true; }
            if (s.GenderOptions == null || s.GenderOptions.Count == 0) { s.GenderOptions = new List<string> { "ذكر", "أنثى" }; changed = true; }
            if (s.BloodTypes == null || s.BloodTypes.Count == 0) { s.BloodTypes = new List<string> { "غير محدد", "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" }; changed = true; }
            return changed;
        }

        private static void NormalizeLookups(AppSettings s)
        {
            s.VisitTypes = CleanLookup(s.VisitTypes, "أنواع الزيارة"); s.AppointmentStatuses = CleanLookup(s.AppointmentStatuses, "حالات الموعد");
            s.TaskPriorities = CleanLookup(s.TaskPriorities, "أولويات المهام"); s.GenderOptions = CleanLookup(s.GenderOptions, "خيارات الجنس"); s.BloodTypes = CleanLookup(s.BloodTypes, "فصائل الدم");
            if (!s.AppointmentStatuses.Contains("حضر") || !s.AppointmentStatuses.Contains("ملغي")) throw new InvalidOperationException("حالات الموعد يجب أن تتضمن «حضر» و«ملغي» لسلامة الجرد والتعارضات.");
        }

        private static List<string> CleanLookup(IEnumerable<string> values, string label)
        {
            var list = (values ?? Enumerable.Empty<string>()).Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(30).ToList();
            if (list.Count == 0) throw new InvalidOperationException(label + " لا يمكن أن تكون فارغة.");
            if (list.Any(x => x.Length > 50)) throw new InvalidOperationException("قيمة في " + label + " أطول من 50 حرفًا."); return list;
        }

        public Patient AddPatient(Patient patient)
        {
            RequireWrite(); if (patient == null) throw new ArgumentNullException(nameof(patient));
            NormalizePatient(patient);
            return InTransaction(() =>
            {
                if (CountAllPatients() >= MaxPatients) throw new InvalidOperationException("وصل النظام إلى الحد الإداري المحدد وهو 10,000 مراجع.");
                if (FindByNationalId(patient.NationalId, true) != null) throw new InvalidOperationException("يوجد مراجع مسجل مسبقًا بنفس رقم الهوية/الإقامة.");
                Patient? likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, null); if (likely != null) throw new DuplicatePatientException(likely);
                AppSettings settings = GetSettings(); patient.Id = Guid.NewGuid(); patient.FileNumber = settings.NextFileNumber; patient.CreatedAt = DateTime.Now; patient.UpdatedAt = patient.CreatedAt; patient.IsArchived = false;
                InsertPatient(patient); settings.NextFileNumber++; settings.UpdatedAt = DateTime.Now; UpsertSettingsInternal(settings); AuditInternal("إضافة مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName); return patient;
            }, true);
        }

        public void UpdatePatient(Patient patient)
        {
            RequireWrite(); if (patient == null) throw new ArgumentNullException(nameof(patient)); Patient? existing = GetPatient(patient.Id);
            if (existing == null) throw new InvalidOperationException("تعذر العثور على ملف المراجع.");
            if (existing.IsArchived) throw new InvalidOperationException("الملف مؤرشف ولا يمكن تعديله قبل استعادته بواسطة المدير.");
            patient.FileNumber = existing.FileNumber; patient.CreatedAt = existing.CreatedAt; patient.IsArchived = existing.IsArchived; patient.ArchivedAt = existing.ArchivedAt; patient.ArchiveReason = existing.ArchiveReason;
            NormalizePatient(patient); Patient? sameId = FindByNationalId(patient.NationalId, true);
            if (sameId != null && sameId.Id != patient.Id) throw new InvalidOperationException("رقم الهوية/الإقامة مستخدم في ملف آخر رقم " + sameId.FileNumber + ".");
            Patient? likely = FindLikelyDuplicate(patient.FullName, patient.DateOfBirth, patient.Mobile, patient.Id); if (likely != null) throw new DuplicatePatientException(likely);
            InTransaction(() => { patient.UpdatedAt = DateTime.Now; UpdatePatientRow(patient); SyncPatientSnapshot(patient); AuditInternal("تعديل مراجع", "Patient", patient.Id.ToString(), patient.FileNumber, patient.FullName); }, true);
        }

        private static void NormalizePatient(Patient p)
        {
            p.IdentityType = (p.IdentityType ?? string.Empty).Trim();
            if (p.IdentityType != "هوية وطنية" && p.IdentityType != "إقامة") throw new InvalidOperationException("اختر نوع الهوية: هوية وطنية أو إقامة.");
            p.NationalId = SaudiValidation.NormalizeDigits(p.NationalId);
            if (!SaudiValidation.ValidateSaudiIdentity(p.NationalId, p.IdentityType, out string identityError)) throw new InvalidOperationException(identityError);
            p.FullName = (p.FullName ?? string.Empty).Trim();
            p.NormalizedName = SaudiValidation.NormalizeArabicName(p.FullName);
            if (p.NormalizedName.Length < 3) throw new InvalidOperationException("اسم المراجع يجب ألا يقل عن ثلاثة أحرف.");
            if (p.FullName.Length > 150) throw new InvalidOperationException("اسم المراجع يتجاوز 150 حرفًا.");
            p.Mobile = SaudiValidation.NormalizeSaudiMobile(p.Mobile);
            if (!SaudiValidation.ValidateSaudiMobile(p.Mobile, true, out string mobileError)) throw new InvalidOperationException(mobileError);
            p.City = (p.City ?? string.Empty).Trim();
            if (p.City.Length < 2 || p.City.Length > 80) throw new InvalidOperationException("أدخل المدينة بصورة صحيحة.");
            if (p.DateOfBirth.HasValue)
            {
                p.DateOfBirth = p.DateOfBirth.Value.Date;
                if (p.DateOfBirth.Value > DateTime.Today) throw new InvalidOperationException("تاريخ الميلاد لا يمكن أن يكون في المستقبل.");
            }
            p.Nationality = (p.Nationality ?? string.Empty).Trim();
            p.Gender = (p.Gender ?? string.Empty).Trim();
            p.AlternatePhone = SaudiValidation.NormalizeDigits(p.AlternatePhone).Trim();
            p.Address = (p.Address ?? string.Empty).Trim();
            p.EmergencyContact = (p.EmergencyContact ?? string.Empty).Trim();
            p.EmergencyPhone = SaudiValidation.NormalizeDigits(p.EmergencyPhone).Trim();
            p.BloodType = (p.BloodType ?? string.Empty).Trim();
            p.Allergies = (p.Allergies ?? string.Empty).Trim();
            p.ChronicConditions = (p.ChronicConditions ?? string.Empty).Trim();
            p.Notes = (p.Notes ?? string.Empty).Trim();
        }

        private void InsertPatient(Patient p)
        {
            Execute(@"INSERT INTO patients(id,file_number,national_id,normalized_name,mobile,city,date_of_birth_ticks,created_ticks,last_visit_ticks,is_archived,payload)
VALUES($id,$file,$national,$name,$mobile,$city,$birth,$created,$last,$archived,$payload);", PatientParameters(p));
        }

        private void UpdatePatientRow(Patient p)
        {
            int changed = Execute(@"UPDATE patients SET national_id=$national,normalized_name=$name,mobile=$mobile,city=$city,date_of_birth_ticks=$birth,created_ticks=$created,last_visit_ticks=$last,is_archived=$archived,payload=$payload WHERE id=$id;", PatientParameters(p));
            if (changed != 1) throw new InvalidOperationException("تعذر العثور على ملف المراجع.");
        }

        private (string, object)[] PatientParameters(Patient p) => new[]
        {
            ("$id", (object)p.Id.ToString("N")), ("$file", p.FileNumber), ("$national", p.NationalId ?? ""), ("$name", p.NormalizedName ?? ""),
            ("$mobile", p.Mobile ?? ""), ("$city", p.City ?? ""), ("$birth", DbTicks(p.DateOfBirth)), ("$created", p.CreatedAt.Ticks),
            ("$last", DbTicks(p.LastVisitAt)), ("$archived", p.IsArchived ? 1 : 0), ("$payload", Serialize(p))
        };

        private void SyncPatientSnapshot(Patient p)
        {
            foreach (Appointment a in QueryPayload<Appointment>("SELECT payload FROM appointments WHERE patient_id=$id;", ("$id", p.Id.ToString("N")))) { a.PatientName = p.FullName; a.FileNumber = p.FileNumber; UpdateAppointmentRow(a); }
            foreach (PatientTask t in QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE patient_id=$id;", ("$id", p.Id.ToString("N")))) { t.PatientName = p.FullName; t.FileNumber = p.FileNumber; UpdateTaskRow(t); }
        }

        public void ArchivePatient(Guid id, string reason) { ArchivePatient(id, reason, true); }
        public void ArchivePatient(Guid id, string reason, bool closeFutureItems)
        {
            RequireAdmin(); Patient? p = GetPatient(id); if (p == null) return;
            InTransaction(() =>
            {
                p.IsArchived = true; p.ArchivedAt = DateTime.Now; p.ArchiveReason = (reason ?? "").Trim(); p.UpdatedAt = DateTime.Now; UpdatePatientRow(p);
                int closedAppointments = 0, closedTasks = 0;
                if (closeFutureItems)
                {
                    foreach (Appointment a in QueryPayload<Appointment>("SELECT payload FROM appointments WHERE patient_id=$id AND is_deleted=0 AND starts_ticks >= $now;", ("$id", id.ToString("N")), ("$now", DateTime.Now.Ticks)).Where(x => x.Status != "ملغي")) { a.Status = "ملغي"; a.UpdatedAt = DateTime.Now; UpdateAppointmentRow(a); closedAppointments++; }
                    foreach (PatientTask t in QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE patient_id=$id AND is_deleted=0 AND is_completed=0;", ("$id", id.ToString("N")))) { t.IsCompleted = true; t.UpdatedAt = DateTime.Now; UpdateTaskRow(t); closedTasks++; }
                }
                AuditInternal("أرشفة مراجع", "Patient", p.Id.ToString(), p.FileNumber, p.ArchiveReason + "؛ أغلقت " + closedAppointments + " موعد و" + closedTasks + " مهمة");
            }, true);
        }

        public void RestorePatient(Guid id) { RequireAdmin(); Patient? p = GetPatient(id); if (p == null) return; p.IsArchived = false; p.ArchivedAt = null; p.ArchiveReason = ""; p.UpdatedAt = DateTime.Now; InTransaction(() => { UpdatePatientRow(p); AuditInternal("استعادة مراجع", "Patient", p.Id.ToString(), p.FileNumber, p.FullName); }, true); }
        public Patient? GetPatient(Guid id) { return QueryPayload<Patient>("SELECT payload FROM patients WHERE id=$id;", ("$id", id.ToString("N"))).FirstOrDefault(); }
        public Patient? FindByFileNumber(long number, bool includeArchived) { return QueryPayload<Patient>("SELECT payload FROM patients WHERE file_number=$n" + (includeArchived ? "" : " AND is_archived=0") + ";", ("$n", number)).FirstOrDefault(); }
        public Patient? FindByNationalId(string id, bool includeArchived) { string value = SaudiValidation.NormalizeDigits(id); return QueryPayload<Patient>("SELECT payload FROM patients WHERE national_id=$id" + (includeArchived ? "" : " AND is_archived=0") + ";", ("$id", value)).FirstOrDefault(); }

        private Patient? FindLikelyDuplicate(string name, DateTime? birth, string mobile, Guid? exceptId)
        {
            string normalized = SaudiValidation.NormalizeArabicName(name), phone = SaudiValidation.NormalizeSaudiMobile(mobile); var candidates = new Dictionary<Guid, Patient>();
            foreach (Patient p in QueryPayload<Patient>("SELECT payload FROM patients WHERE normalized_name=$name OR mobile=$mobile OR date_of_birth_ticks=$birth;", ("$name", normalized), ("$mobile", phone), ("$birth", DbTicks(birth)))) candidates[p.Id] = p;
            return candidates.Values.FirstOrDefault(p =>
            {
                if (exceptId.HasValue && p.Id == exceptId.Value) return false; string existingName = SaudiValidation.NormalizeArabicName(p.NormalizedName); bool sameBirth = birth.HasValue && p.DateOfBirth.HasValue && birth.Value.Date == p.DateOfBirth.Value.Date, samePhone = !string.IsNullOrEmpty(phone) && p.Mobile == phone;
                if (normalized == existingName) return sameBirth || samePhone; if (samePhone && NamesLikelyMatch(normalized, existingName)) return true; return sameBirth && SameFirstAndLastName(normalized, existingName);
            });
        }

        private static bool NamesLikelyMatch(string left, string right)
        {
            string a = (left ?? "").Replace(" ", ""), b = SaudiValidation.NormalizeArabicName(right).Replace(" ", ""); if (a == b) return true; if (a.Length < 5 || b.Length < 5 || Math.Abs(a.Length - b.Length) > 2) return false;
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray(), current = new int[b.Length + 1]; for (int i = 1; i <= a.Length; i++) { current[0] = i; for (int j = 1; j <= b.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)); var swap = previous; previous = current; current = swap; } return previous[b.Length] <= 2;
        }

        private static bool SameFirstAndLastName(string left, string right)
        {
            string[] a = (left ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), b = (right ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); return a.Length >= 2 && b.Length >= 2 && a[0] == b[0] && a[a.Length - 1] == b[b.Length - 1];
        }

        public List<Patient> SearchPatients(string mode, string term, bool includeArchived, string sort)
        {
            string q = (term ?? "").Trim(), digits = SaudiValidation.NormalizeDigits(q), name = SaudiValidation.NormalizeArabicName(q), phone = SaudiValidation.NormalizeSaudiMobile(q), where; var parameters = new List<(string, object)>();
            if (q.Length == 0) where = "1=1";
            else if (mode == "رقم الملف") { if (!long.TryParse(digits, out long n)) return new List<Patient>(); where = "file_number=$file"; parameters.Add(("$file", n)); }
            else if (mode == "الهوية/الإقامة") { where = "national_id=$national"; parameters.Add(("$national", digits)); }
            else if (mode == "رقم الجوال") { where = "mobile=$mobile"; parameters.Add(("$mobile", phone)); }
            else if (mode == "المدينة") { where = "city LIKE $city"; parameters.Add(("$city", "%" + q + "%")); }
            else if (mode == "الاسم") { where = "normalized_name LIKE $name"; parameters.Add(("$name", "%" + name + "%")); }
            else { where = "normalized_name LIKE $name OR mobile=$mobile OR national_id=$national"; parameters.Add(("$name", "%" + name + "%")); parameters.Add(("$mobile", phone)); parameters.Add(("$national", digits)); if (long.TryParse(digits, out long n)) { where += " OR file_number=$file"; parameters.Add(("$file", n)); } }
            if (!includeArchived) where = "(" + where + ") AND is_archived=0";
            string orderBy = sort switch
            {
                "الاسم" => "normalized_name COLLATE NOCASE, file_number",
                "الأحدث" => "created_ticks DESC, file_number DESC",
                "آخر مراجعة" => "last_visit_ticks DESC, file_number",
                _ => "file_number"
            };
            return QueryPayload<Patient>("SELECT payload FROM patients WHERE " + where + " ORDER BY " + orderBy + " LIMIT $limit;", parameters.Concat(new[] { ("$limit", (object)(q.Length == 0 ? DefaultPatientListLimit : MaxPatients)) }).ToArray());
        }

        public int CountActivePatients() { return checked((int)ScalarLong("SELECT COUNT(*) FROM patients WHERE is_archived=0;")); }
        public int CountAllPatients() { return checked((int)ScalarLong("SELECT COUNT(*) FROM patients;")); }
        public IEnumerable<Patient> GetAllPatients(bool includeArchived) { return QueryPayload<Patient>("SELECT payload FROM patients" + (includeArchived ? "" : " WHERE is_archived=0") + " ORDER BY file_number;"); }

        public PatientAttachment AddAttachment(Guid patientId, string sourcePath, string category)
        {
            RequireWrite(); Patient? patient = GetPatient(patientId); if (patient == null) throw new InvalidOperationException("ملف المراجع غير موجود."); if (patient.IsArchived) throw new InvalidOperationException("لا يمكن إضافة مرفق إلى ملف مؤرشف قبل استعادته.");
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("تعذر العثور على الملف المحدد."); string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!AllowedAttachmentExtensions.Contains(extension)) throw new InvalidOperationException("نوع الملف غير مسموح. الأنواع المقبولة: PDF وJPG وPNG وDOCX.");
            var file = new FileInfo(sourcePath); if (file.Length <= 0) throw new InvalidOperationException("الملف المحدد فارغ."); if (file.Length > MaxAttachmentBytes) throw new InvalidOperationException("حجم المرفق يتجاوز الحد الأقصى 10 ميجابايت."); ValidateAttachmentSignature(sourcePath, extension);
            byte[] content = File.ReadAllBytes(sourcePath); var a = new PatientAttachment { Id = Guid.NewGuid(), PatientId = patient.Id, FileNumber = patient.FileNumber, OriginalName = Path.GetFileName(sourcePath), StoredId = "sqlite:" + Guid.NewGuid().ToString("N"), ContentType = ContentType(extension), SizeBytes = file.Length, Sha256 = HashBytes(content), Category = string.IsNullOrWhiteSpace(category) ? "أخرى" : category.Trim(), UploadedAt = DateTime.Now, UploadedBy = currentUser, IsDeleted = false };
            InTransaction(() => { InsertAttachment(a, content); AuditInternal("إضافة مرفق", "Attachment", a.Id.ToString(), patient.FileNumber, a.OriginalName); }, true); return a;
        }

        private void InsertAttachment(PatientAttachment a, byte[] content)
        {
            Execute("INSERT INTO attachments(id,patient_id,file_number,is_deleted,deleted_ticks,payload,content) VALUES($id,$patient,$file,$deleted,$deletedAt,$payload,$content);", ("$id", a.Id.ToString("N")), ("$patient", a.PatientId.ToString("N")), ("$file", a.FileNumber), ("$deleted", a.IsDeleted ? 1 : 0), ("$deletedAt", DbTicks(a.DeletedAt)), ("$payload", Serialize(a)), ("$content", content));
        }

        private void UpdateAttachmentRow(PatientAttachment a)
        {
            Execute("UPDATE attachments SET is_deleted=$deleted,deleted_ticks=$deletedAt,payload=$payload WHERE id=$id;", ("$deleted", a.IsDeleted ? 1 : 0), ("$deletedAt", DbTicks(a.DeletedAt)), ("$payload", Serialize(a)), ("$id", a.Id.ToString("N")));
        }

        private PatientAttachment? GetAttachment(Guid id) { return QueryPayload<PatientAttachment>("SELECT payload FROM attachments WHERE id=$id;", ("$id", id.ToString("N"))).FirstOrDefault(); }
        public List<PatientAttachment> GetAttachments(Guid patientId, bool includeDeleted) { return QueryPayload<PatientAttachment>("SELECT payload FROM attachments WHERE patient_id=$id" + (includeDeleted ? "" : " AND is_deleted=0") + ";", ("$id", patientId.ToString("N"))).OrderByDescending(x => x.UploadedAt).ToList(); }
        public void DeleteAttachment(Guid id) { RequireWrite(); PatientAttachment? a = GetAttachment(id); if (a == null || a.IsDeleted) return; a.IsDeleted = true; a.DeletedAt = DateTime.Now; a.DeletedBy = currentUser; InTransaction(() => { UpdateAttachmentRow(a); AuditInternal("نقل مرفق إلى المحذوفات", "Attachment", id.ToString(), a.FileNumber, a.OriginalName); }, true); }
        public void RestoreAttachment(Guid id) { RequireWrite(); PatientAttachment? a = GetAttachment(id); if (a == null || !a.IsDeleted) return; if (ScalarLong("SELECT COUNT(*) FROM attachments WHERE id=$id AND length(content)>0;", ("$id", id.ToString("N"))) != 1) throw new InvalidDataException("ملف المرفق الداخلي غير موجود."); a.IsDeleted = false; a.DeletedAt = null; a.DeletedBy = ""; InTransaction(() => { UpdateAttachmentRow(a); AuditInternal("استعادة مرفق", "Attachment", id.ToString(), a.FileNumber, a.OriginalName); }, true); }

        public string ExportAttachmentToTemporaryFile(Guid id)
        {
            PatientAttachment? a = GetAttachment(id); if (a == null || a.IsDeleted) throw new InvalidOperationException("المرفق غير متاح."); byte[] content;
            using (SqliteCommand cmd = Command("SELECT content FROM attachments WHERE id=$id;", ("$id", id.ToString("N")))) { object? value = cmd.ExecuteScalar(); if (value == null || value == DBNull.Value) throw new InvalidDataException("ملف المرفق الداخلي غير موجود."); content = (byte[])value; }
            if (!string.Equals(HashBytes(content), a.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("فشل فحص سلامة المرفق."); EnsurePrivateDirectory(TemporaryAttachmentDirectory); string output = Path.Combine(TemporaryAttachmentDirectory, a.Id.ToString("N") + "_" + SafeFileName(a.OriginalName)); File.WriteAllBytes(output, content); TryRestrictFileToCurrentUser(output); TryMarkTemporary(output); Audit("فتح مرفق", "Attachment", id.ToString(), a.FileNumber, a.OriginalName); Checkpoint(); return output;
        }

        public static void ScheduleTemporaryAttachmentCleanup(string path, Process viewer)
        {
            if (string.IsNullOrWhiteSpace(path) || viewer == null) return; try { viewer.EnableRaisingEvents = true; viewer.Exited += async delegate { await Task.Delay(3000).ConfigureAwait(false); SecureDeleteFile(path); }; if (viewer.HasExited) Task.Run(async delegate { await Task.Delay(3000).ConfigureAwait(false); SecureDeleteFile(path); }); } catch { }
        }

        public static void CleanupTemporaryAttachments()
        {
            try { foreach (string folder in Directory.EnumerateDirectories(Path.GetTempPath(), TemporaryAttachmentPrefix + "*")) SecureDeleteDirectory(folder); string oldFolder = Path.Combine(Path.GetTempPath(), "SaudiPatientRecordsView"); if (Directory.Exists(oldFolder)) SecureDeleteDirectory(oldFolder); } catch { }
        }

        public static void TryRestrictFileToCurrentUser(string path)
        {
            try { SecurityIdentifier sid = WindowsIdentity.GetCurrent().User; if (sid == null) return; var rules = new FileSecurity(); rules.SetOwner(sid); rules.SetAccessRuleProtection(true, false); rules.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow)); new FileInfo(path).SetAccessControl(rules); } catch { }
        }

        private static void EnsurePrivateDirectory(string path)
        {
            Directory.CreateDirectory(path); try { SecurityIdentifier sid = WindowsIdentity.GetCurrent().User; if (sid == null) return; var rules = new DirectorySecurity(); rules.SetOwner(sid); rules.SetAccessRuleProtection(true, false); rules.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow)); new DirectoryInfo(path).SetAccessControl(rules); } catch { }
        }

        private static void TryMarkTemporary(string path) { try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Temporary | FileAttributes.NotContentIndexed); } catch { } }
        private static void SecureDeleteDirectory(string folder) { try { foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) SecureDeleteFile(file); Directory.Delete(folder, true); } catch { } }
        private static void SecureDeleteFile(string path)
        {
            try { if (!File.Exists(path)) return; long length = new FileInfo(path).Length; using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) { byte[] buffer = new byte[81920]; long remaining = length; while (remaining > 0) { int count = (int)Math.Min(buffer.Length, remaining); RandomNumberGenerator.Fill(buffer.AsSpan(0, count)); stream.Write(buffer, 0, count); remaining -= count; } stream.Flush(true); stream.Position = 0; Array.Clear(buffer, 0, buffer.Length); remaining = length; while (remaining > 0) { int count = (int)Math.Min(buffer.Length, remaining); stream.Write(buffer, 0, count); remaining -= count; } stream.Flush(true); CryptographicOperations.ZeroMemory(buffer); } File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } catch { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        }

        private static void ValidateAttachmentSignature(string path, string extension)
        {
            byte[] header = new byte[8]; int read; using (var input = File.OpenRead(path)) read = input.Read(header, 0, header.Length); bool valid = false;
            if (extension == ".pdf") valid = read >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D;
            else if (extension == ".jpg" || extension == ".jpeg") valid = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            else if (extension == ".png") valid = read >= 8 && header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            else if (extension == ".docx") { try { using (ZipArchive archive = ZipFile.OpenRead(path)) valid = archive.GetEntry("[Content_Types].xml") != null && archive.GetEntry("word/document.xml") != null; } catch (InvalidDataException) { valid = false; } }
            if (!valid) throw new InvalidDataException("محتوى الملف لا يطابق امتداده أو أن الملف تالف.");
        }

        public int PurgeDeletedAttachments(DateTime deletedBefore)
        {
            RequireAdmin(); int purged = 0; InTransaction(() => { purged = Execute("DELETE FROM attachments WHERE is_deleted=1 AND deleted_ticks IS NOT NULL AND deleted_ticks < $cutoff;", ("$cutoff", deletedBefore.Ticks)); AuditInternal("إتلاف نهائي لمرفقات محذوفة", "Attachment", "purge", null, "العدد: " + purged); }); Checkpoint(); return purged;
        }

        private static string SafeFileName(string value) { string name = Path.GetFileName(value ?? "مرفق"); foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return string.IsNullOrWhiteSpace(name) ? "مرفق" : name; }
        private static string ContentType(string extension) { if (extension == ".pdf") return "application/pdf"; if (extension == ".jpg" || extension == ".jpeg") return "image/jpeg"; if (extension == ".png") return "image/png"; return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"; }
        private static string HashBytes(byte[] content) { using (var sha = SHA256.Create()) return Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant(); }

        public Appointment AddAppointment(Appointment a)
        {
            RequireWrite(); if (a == null) throw new ArgumentNullException(nameof(a)); return InTransaction(() => { ValidateAppointmentAvailability(a); a.Id = Guid.NewGuid(); a.CreatedAt = DateTime.Now; a.UpdatedAt = a.CreatedAt; a.IsDeleted = false; InsertAppointment(a); RecalculateLastVisit(a.PatientId); AuditInternal("إضافة موعد", "Appointment", a.Id.ToString(), a.FileNumber, a.Title); return a; }, true);
        }

        public void UpdateAppointment(Appointment a)
        {
            RequireWrite(); if (a == null) throw new ArgumentNullException(nameof(a)); InTransaction(() => { Appointment? old = GetAppointment(a.Id); if (old == null) throw new InvalidOperationException("الموعد غير موجود."); ValidateAppointmentAvailability(a); if (old.StartsAt != a.StartsAt || (old.Status != a.Status && a.Status != "ملغي")) a.ReminderNotifiedAt = null; a.CreatedAt = old.CreatedAt; a.UpdatedAt = DateTime.Now; UpdateAppointmentRow(a); RecalculateLastVisit(a.PatientId); if (old.PatientId != a.PatientId) RecalculateLastVisit(old.PatientId); AuditInternal("تعديل موعد", "Appointment", a.Id.ToString(), a.FileNumber, a.Title); }, true);
        }

        public void DeleteAppointment(Guid id) { RequireAdmin(); Appointment? a = GetAppointment(id); if (a == null || a.IsDeleted) return; a.IsDeleted = true; a.DeletedAt = DateTime.Now; a.DeletedBy = currentUser; a.UpdatedAt = DateTime.Now; InTransaction(() => { UpdateAppointmentRow(a); RecalculateLastVisit(a.PatientId); AuditInternal("نقل موعد إلى المحذوفات", "Appointment", id.ToString(), a.FileNumber, a.Title); }, true); }
        public void RestoreAppointment(Guid id) { RequireAdmin(); Appointment? a = GetAppointment(id); if (a == null || !a.IsDeleted) return; a.IsDeleted = false; a.DeletedAt = null; a.DeletedBy = ""; a.UpdatedAt = DateTime.Now; InTransaction(() => { ValidateAppointmentAvailability(a); UpdateAppointmentRow(a); RecalculateLastVisit(a.PatientId); AuditInternal("استعادة موعد", "Appointment", id.ToString(), a.FileNumber, a.Title); }, true); }
        public Appointment? GetAppointment(Guid id) { return QueryPayload<Appointment>("SELECT payload FROM appointments WHERE id=$id;", ("$id", id.ToString("N"))).FirstOrDefault(); }

        private void InsertAppointment(Appointment a) { Execute("INSERT INTO appointments(id,patient_id,file_number,starts_ticks,duration_minutes,status,is_deleted,reminder_ticks,deleted_ticks,payload) VALUES($id,$patient,$file,$start,$duration,$status,$deleted,$reminder,$deletedAt,$payload);", AppointmentParameters(a)); }
        private void UpdateAppointmentRow(Appointment a) { Execute("UPDATE appointments SET patient_id=$patient,file_number=$file,starts_ticks=$start,duration_minutes=$duration,status=$status,is_deleted=$deleted,reminder_ticks=$reminder,deleted_ticks=$deletedAt,payload=$payload WHERE id=$id;", AppointmentParameters(a)); }
        private (string, object)[] AppointmentParameters(Appointment a) => new[] { ("$id", (object)a.Id.ToString("N")), ("$patient", a.PatientId.ToString("N")), ("$file", a.FileNumber), ("$start", a.StartsAt.Ticks), ("$duration", a.DurationMinutes), ("$status", a.Status ?? ""), ("$deleted", a.IsDeleted ? 1 : 0), ("$reminder", DbTicks(a.ReminderNotifiedAt)), ("$deletedAt", DbTicks(a.DeletedAt)), ("$payload", Serialize(a)) };

        public void ValidateAppointmentAvailability(Appointment a)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); if (a.IsDeleted) return;
            Patient? patient = GetPatient(a.PatientId); if (patient == null) throw new InvalidOperationException("ملف المراجع غير موجود."); if (patient.IsArchived) throw new InvalidOperationException("لا يمكن حجز أو تعديل موعد لملف مؤرشف قبل استعادته.");
            a.FileNumber = patient.FileNumber; a.PatientName = patient.FullName;
            a.Title = (a.Title ?? string.Empty).Trim(); if (a.Title.Length < 2 || a.Title.Length > 120) throw new InvalidOperationException("عنوان الموعد يجب أن يكون بين حرفين و120 حرفًا.");
            AppSettings settings = GetSettings(); if (!settings.VisitTypes.Contains(a.VisitType)) throw new InvalidOperationException("نوع الزيارة غير صحيح."); if (!settings.AppointmentStatuses.Contains(a.Status)) throw new InvalidOperationException("حالة الموعد غير صحيحة.");
            if (a.DurationMinutes < 5 || a.DurationMinutes > 12 * 60) throw new InvalidOperationException("مدة الموعد غير صحيحة.");
            Appointment? stored = a.Id == Guid.Empty ? null : GetAppointment(a.Id); bool unchangedPastTime = stored != null && stored.StartsAt == a.StartsAt; if (!unchangedPastTime && a.StartsAt < DateTime.Now.AddMinutes(-1)) throw new InvalidOperationException("لا يمكن إنشاء أو نقل موعد إلى وقت سابق."); ValidateAppointmentSlot(a.StartsAt, a.DurationMinutes, a.Status, a.Id);
        }

        private void ValidateAppointmentSlot(DateTime startsAt, int durationMinutes, string status, Guid ignoredAppointmentId)
        {
            if (!SaudiValidation.IsOfficialWorkingDay(startsAt)) throw new InvalidOperationException("لا يمكن حجز موعد يوم الجمعة أو السبت."); if (ScalarLong("SELECT COUNT(*) FROM closures WHERE date_ticks=$date;", ("$date", startsAt.Date.Ticks)) > 0) throw new InvalidOperationException("هذا اليوم مسجل كإجازة أو يوم إغلاق للمنشأة."); AppSettings s = GetSettings(); int start = startsAt.Hour * 60 + startsAt.Minute, endMinutes = start + durationMinutes; if (start < s.WorkDayStartMinutes || endMinutes > s.WorkDayEndMinutes) throw new InvalidOperationException("الموعد خارج ساعات الدوام المحددة في الإعدادات."); if (status == "ملغي") return; DateTime end = startsAt.AddMinutes(durationMinutes), dayEnd = startsAt.Date.AddDays(1);
            Appointment? conflict = QueryPayload<Appointment>("SELECT payload FROM appointments WHERE starts_ticks >= $dayStart AND starts_ticks < $dayEnd AND is_deleted=0;", ("$dayStart", startsAt.Date.Ticks), ("$dayEnd", dayEnd.Ticks)).FirstOrDefault(x => x.Id != ignoredAppointmentId && x.Status != "ملغي" && x.StartsAt < end && x.StartsAt.AddMinutes(x.DurationMinutes) > startsAt); if (conflict != null) throw new AppointmentConflictException(conflict);
        }

        public List<Appointment> GetAppointments(DateTime? from, DateTime? to) { return GetAppointments(from, to, false); }
        public List<Appointment> GetAppointments(DateTime? from, DateTime? to, bool includeDeleted)
        {
            var where = new List<string>(); var p = new List<(string, object)>(); if (from.HasValue) { where.Add("starts_ticks >= $from"); p.Add(("$from", from.Value.Ticks)); } if (to.HasValue) { where.Add("starts_ticks < $to"); p.Add(("$to", to.Value.Ticks)); } if (!includeDeleted) where.Add("is_deleted=0"); string sql = "SELECT payload FROM appointments" + (where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where)) + " ORDER BY starts_ticks;"; return QueryPayload<Appointment>(sql, p.ToArray());
        }
        public List<Appointment> GetPatientAppointments(Guid patientId) { return QueryPayload<Appointment>("SELECT payload FROM appointments WHERE patient_id=$id AND is_deleted=0 ORDER BY starts_ticks DESC;", ("$id", patientId.ToString("N"))); }
        public List<Appointment> GetDeletedAppointments() { return QueryPayload<Appointment>("SELECT payload FROM appointments WHERE is_deleted=1 ORDER BY deleted_ticks DESC;"); }
        public DateTime GetNextAvailableAppointmentTime(int durationMinutes)
        {
            if (durationMinutes <= 0 || durationMinutes > 12 * 60) throw new ArgumentOutOfRangeException(nameof(durationMinutes), "مدة الموعد غير صحيحة."); AppSettings s = GetSettings(); DateTime now = DateTime.Now; int rounded = ((now.Minute + 14) / 15) * 15; DateTime candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddMinutes(rounded); if (candidate.Hour * 60 + candidate.Minute < s.WorkDayStartMinutes) candidate = candidate.Date.AddMinutes(s.WorkDayStartMinutes);
            for (int i = 0; i < 365 * 24 * 4; i++) { int minute = candidate.Hour * 60 + candidate.Minute; if (minute + durationMinutes > s.WorkDayEndMinutes) candidate = candidate.Date.AddDays(1).AddMinutes(s.WorkDayStartMinutes); if (!SaudiValidation.IsOfficialWorkingDay(candidate) || ScalarLong("SELECT COUNT(*) FROM closures WHERE date_ticks=$date;", ("$date", candidate.Date.Ticks)) > 0) { candidate = candidate.Date.AddDays(1).AddMinutes(s.WorkDayStartMinutes); continue; } try { ValidateAppointmentSlot(candidate, durationMinutes, "مؤكد", Guid.Empty); return candidate; } catch (AppointmentConflictException) { candidate = candidate.AddMinutes(15); } }
            throw new InvalidOperationException("لم يتم العثور على وقت متاح خلال سنة.");
        }

        public PatientTask AddTask(PatientTask t) { RequireWrite(); ValidateTask(t); t.Id = Guid.NewGuid(); t.CreatedAt = DateTime.Now; t.UpdatedAt = t.CreatedAt; t.IsDeleted = false; InTransaction(() => { InsertTask(t); AuditInternal("إضافة مهمة", "Task", t.Id.ToString(), t.FileNumber, t.Title); }, true); return t; }
        public void UpdateTask(PatientTask t) { RequireWrite(); ValidateTask(t); PatientTask? old = GetTask(t.Id); if (old == null) throw new InvalidOperationException("المهمة غير موجودة."); if (old.DueAt != t.DueAt || (old.IsCompleted && !t.IsCompleted)) t.ReminderNotifiedAt = null; t.CreatedAt = old.CreatedAt; t.UpdatedAt = DateTime.Now; InTransaction(() => { UpdateTaskRow(t); AuditInternal("تعديل مهمة", "Task", t.Id.ToString(), t.FileNumber, t.Title); }, true); }
        public void DeleteTask(Guid id) { RequireAdmin(); PatientTask? t = GetTask(id); if (t == null || t.IsDeleted) return; t.IsDeleted = true; t.DeletedAt = DateTime.Now; t.DeletedBy = currentUser; t.UpdatedAt = DateTime.Now; InTransaction(() => { UpdateTaskRow(t); AuditInternal("نقل مهمة إلى المحذوفات", "Task", id.ToString(), t.FileNumber, t.Title); }, true); }
        public void RestoreTask(Guid id) { RequireAdmin(); PatientTask? t = GetTask(id); if (t == null || !t.IsDeleted) return; t.IsDeleted = false; t.DeletedAt = null; t.DeletedBy = ""; t.UpdatedAt = DateTime.Now; InTransaction(() => { UpdateTaskRow(t); AuditInternal("استعادة مهمة", "Task", id.ToString(), t.FileNumber, t.Title); }, true); }
        private PatientTask? GetTask(Guid id) { return QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE id=$id;", ("$id", id.ToString("N"))).FirstOrDefault(); }
        private void ValidateTask(PatientTask t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            t.Title = (t.Title ?? string.Empty).Trim(); if (t.Title.Length < 2 || t.Title.Length > 120) throw new InvalidOperationException("عنوان المهمة يجب أن يكون بين حرفين و120 حرفًا.");
            Patient? p = GetPatient(t.PatientId); if (p == null) throw new InvalidOperationException("ملف المراجع غير موجود."); if (p.IsArchived) throw new InvalidOperationException("لا يمكن إضافة أو تعديل مهمة لملف مؤرشف قبل استعادته.");
            t.FileNumber = p.FileNumber; t.PatientName = p.FullName;
            if (!GetSettings().TaskPriorities.Contains(t.Priority)) throw new InvalidOperationException("أولوية المهمة غير صحيحة.");
        }
        private void InsertTask(PatientTask t) { Execute("INSERT INTO tasks(id,patient_id,file_number,due_ticks,is_completed,is_deleted,reminder_ticks,deleted_ticks,payload) VALUES($id,$patient,$file,$due,$completed,$deleted,$reminder,$deletedAt,$payload);", TaskParameters(t)); }
        private void UpdateTaskRow(PatientTask t) { Execute("UPDATE tasks SET patient_id=$patient,file_number=$file,due_ticks=$due,is_completed=$completed,is_deleted=$deleted,reminder_ticks=$reminder,deleted_ticks=$deletedAt,payload=$payload WHERE id=$id;", TaskParameters(t)); }
        private (string, object)[] TaskParameters(PatientTask t) => new[] { ("$id", (object)t.Id.ToString("N")), ("$patient", t.PatientId.ToString("N")), ("$file", t.FileNumber), ("$due", t.DueAt.Ticks), ("$completed", t.IsCompleted ? 1 : 0), ("$deleted", t.IsDeleted ? 1 : 0), ("$reminder", DbTicks(t.ReminderNotifiedAt)), ("$deletedAt", DbTicks(t.DeletedAt)), ("$payload", Serialize(t)) };
        public List<PatientTask> GetTasks(bool includeCompleted) { return QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE is_deleted=0" + (includeCompleted ? "" : " AND is_completed=0") + " ORDER BY due_ticks;"); }
        public List<PatientTask> GetPatientTasks(Guid id) { return QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE patient_id=$id AND is_deleted=0 ORDER BY due_ticks DESC;", ("$id", id.ToString("N"))); }
        public List<PatientTask> GetDeletedTasks() { return QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE is_deleted=1 ORDER BY deleted_ticks DESC;"); }

        public Appointment? GetNextUnnotifiedAppointment(DateTime from, DateTime to) { return GetUnnotifiedAppointments(from, to, 1).FirstOrDefault(); }
        public PatientTask? GetNextUnnotifiedTask(DateTime from, DateTime to) { return GetUnnotifiedTasks(from, to, 1).FirstOrDefault(); }
        public List<Appointment> GetUnnotifiedAppointments(DateTime from, DateTime to, int maximum) { return QueryPayload<Appointment>("SELECT payload FROM appointments WHERE is_deleted=0 AND reminder_ticks IS NULL AND starts_ticks >= $from AND starts_ticks <= $to AND status <> 'ملغي' ORDER BY starts_ticks LIMIT $limit;", ("$from", from.Ticks), ("$to", to.Ticks), ("$limit", Math.Max(1, maximum))); }
        public List<PatientTask> GetUnnotifiedTasks(DateTime from, DateTime to, int maximum) { return QueryPayload<PatientTask>("SELECT payload FROM tasks WHERE is_deleted=0 AND is_completed=0 AND reminder_ticks IS NULL AND due_ticks >= $from AND due_ticks <= $to ORDER BY due_ticks LIMIT $limit;", ("$from", from.Ticks), ("$to", to.Ticks), ("$limit", Math.Max(1, maximum))); }
        public void MarkAppointmentNotified(Guid id) { Appointment a = GetAppointment(id); if (a != null) { a.ReminderNotifiedAt = DateTime.Now; UpdateAppointmentRow(a); Checkpoint(); } }
        public void MarkTaskNotified(Guid id) { PatientTask t = GetTask(id); if (t != null) { t.ReminderNotifiedAt = DateTime.Now; UpdateTaskRow(t); Checkpoint(); } }

        private void RecalculateLastVisit(Guid patientId)
        {
            Patient? p = GetPatient(patientId); if (p == null) return; Appointment? last = QueryPayload<Appointment>("SELECT payload FROM appointments WHERE patient_id=$id AND is_deleted=0 AND status='حضر' AND starts_ticks <= $now ORDER BY starts_ticks DESC LIMIT 1;", ("$id", patientId.ToString("N")), ("$now", DateTime.Now.Ticks)).FirstOrDefault(); p.LastVisitAt = last?.StartsAt; p.UpdatedAt = DateTime.Now; UpdatePatientRow(p);
        }

        public List<Patient> GetInventoryCandidates(DateTime asOf)
        {
            DateTime cutoff = asOf.Date.AddYears(-10); return QueryPayload<Patient>(@"SELECT p.payload FROM patients p WHERE p.is_archived=0 AND COALESCE((SELECT MAX(a.starts_ticks) FROM appointments a WHERE a.patient_id=p.id AND a.is_deleted=0 AND a.status='حضر' AND a.starts_ticks <= $asOf),p.created_ticks) <= $cutoff ORDER BY p.file_number;", ("$asOf", asOf.Ticks), ("$cutoff", cutoff.Ticks));
        }

        public void SetInventoryAlerted(int year) { AppSettings s = GetSettings(); s.LastInventoryAlertYear = year; s.UpdatedAt = DateTime.Now; UpsertSettingsInternal(s); Checkpoint(); }
        public void UpdateBackupStatus(string status, DateTime? successAt) { AppSettings s = GetSettings(); if (successAt.HasValue) s.LastAutoBackupAt = successAt; s.LastBackupStatus = status ?? ""; s.UpdatedAt = DateTime.Now; UpsertSettingsInternal(s); Checkpoint(); }
        public List<ClosureDate> GetClosures() { return QueryPayload<ClosureDate>("SELECT payload FROM closures ORDER BY date_ticks;"); }
        public void AddClosure(DateTime date, string reason) { RequireAdmin(); if (!SaudiValidation.IsOfficialWorkingDay(date)) throw new InvalidOperationException("الجمعة والسبت مستبعدان أصلًا من المواعيد."); if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("أدخل سبب الإغلاق أو اسم الإجازة."); var c = new ClosureDate { Id = Guid.NewGuid(), Date = date.Date, Reason = reason.Trim(), CreatedAt = DateTime.Now }; InTransaction(() => { Execute("INSERT INTO closures(id,date_ticks,payload) VALUES($id,$date,$payload);", ("$id", c.Id.ToString("N")), ("$date", c.Date.Ticks), ("$payload", Serialize(c))); AuditInternal("إضافة يوم إغلاق", "Closure", c.Id.ToString(), null, c.Date.ToString("yyyy-MM-dd") + " " + c.Reason); }, true); }
        public void DeleteClosure(Guid id) { RequireAdmin(); ClosureDate? c = QueryPayload<ClosureDate>("SELECT payload FROM closures WHERE id=$id;", ("$id", id.ToString("N"))).FirstOrDefault(); if (c == null) return; InTransaction(() => { Execute("DELETE FROM closures WHERE id=$id;", ("$id", id.ToString("N"))); AuditInternal("حذف يوم إغلاق", "Closure", id.ToString(), null, c.Date.ToString("yyyy-MM-dd") + " " + c.Reason); }, true); }

        public void Audit(string action, string entityType, string entityId, long? fileNumber, string details) { AuditInternal(action, entityType, entityId, fileNumber, details); }
        public void AuditSecurityEvent(string userName, string action, string details, DateTime occurredAt) { InsertAudit(new AuditEntry { Id = Guid.NewGuid(), OccurredAt = occurredAt, Action = action, EntityType = "Security", EntityId = userName ?? "", FileNumber = null, Details = details ?? "", MachineName = Environment.MachineName, UserName = string.IsNullOrWhiteSpace(userName) ? "غير معروف" : userName }); Checkpoint(); }
        private void AuditInternal(string action, string entityType, string entityId, long? fileNumber, string details) { InsertAudit(new AuditEntry { Id = Guid.NewGuid(), OccurredAt = DateTime.Now, Action = action, EntityType = entityType, EntityId = entityId, FileNumber = fileNumber, Details = details ?? "", MachineName = Environment.MachineName, UserName = currentUser }); }
        private void InsertAudit(AuditEntry entry) { Execute("INSERT INTO audit(id,occurred_ticks,payload) VALUES($id,$time,$payload);", ("$id", entry.Id.ToString("N")), ("$time", entry.OccurredAt.Ticks), ("$payload", Serialize(entry))); if (ScalarLong("SELECT COUNT(*) FROM audit;") > MaxAuditEntries + 100) TrimAuditIfNeeded(); }
        private void TrimAuditIfNeeded() { long excess = ScalarLong("SELECT COUNT(*) FROM audit;") - MaxAuditEntries; if (excess > 0) Execute("DELETE FROM audit WHERE id IN (SELECT id FROM audit ORDER BY occurred_ticks ASC LIMIT $count);", ("$count", excess)); }
        public List<AuditEntry> GetRecentAudit(int count) { int limit = Math.Max(1, Math.Min(count, 5000)); return QueryPayload<AuditEntry>("SELECT payload FROM audit ORDER BY occurred_ticks DESC LIMIT $limit;", ("$limit", limit)); }
        public List<AuditEntry> GetAuditPage(int skip, int count) { int offset = Math.Max(0, skip), limit = Math.Max(1, Math.Min(count, 5000)); return QueryPayload<AuditEntry>("SELECT payload FROM audit ORDER BY occurred_ticks DESC LIMIT $limit OFFSET $offset;", ("$limit", limit), ("$offset", offset)); }

        public void Checkpoint() { if (db == null || transaction != null) return; try { Execute("PRAGMA wal_checkpoint(PASSIVE);"); } catch { } }

        public void CreateSnapshot(string destinationPath)
        {
            RequireAdmin();
            if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("مسار النسخة غير صالح.", nameof(destinationPath)); Checkpoint();
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            using (var target = new SqliteConnection(ConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate))) { target.Open(); (db ?? throw new ObjectDisposedException(nameof(AppDatabase))).BackupDatabase(target); }
            ValidateDatabaseFile(destinationPath);
        }

        public void ValidateDatabaseFile(string path)
        {
            RequireAdmin();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("ملف قاعدة البيانات غير موجود."); EnsureNativeInitialized();
            using (var test = new SqliteConnection(ConnectionString(path, SqliteOpenMode.ReadOnly))) { test.Open(); using (var cmd = test.CreateCommand()) { cmd.CommandText = "PRAGMA integrity_check;"; string result = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty; if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("فشل فحص سلامة قاعدة SQLite: " + result); cmd.CommandText = "SELECT COUNT(*) FROM settings WHERE id=1;"; if (Convert.ToInt64(cmd.ExecuteScalar()) != 1) throw new InvalidDataException("قاعدة بيانات النسخة لا تحتوي إعدادات النظام."); } }
        }

        private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            SqliteConnection connection = db ?? throw new ObjectDisposedException(nameof(AppDatabase)); var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = transaction; foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value); return cmd;
        }
        private int Execute(string sql, params (string Name, object Value)[] parameters) { using (SqliteCommand cmd = Command(sql, parameters)) return cmd.ExecuteNonQuery(); }
        private long ScalarLong(string sql, params (string Name, object Value)[] parameters) { using (SqliteCommand cmd = Command(sql, parameters)) { object? value = cmd.ExecuteScalar(); return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value); } }
        private string ScalarString(string sql, params (string Name, object Value)[] parameters) { using (SqliteCommand cmd = Command(sql, parameters)) return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty; }
        private List<T> QueryPayload<T>(string sql, params (string Name, object Value)[] parameters)
        {
            var result = new List<T>(); using (SqliteCommand cmd = Command(sql, parameters)) using (SqliteDataReader reader = cmd.ExecuteReader()) while (reader.Read()) { T item = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions); if (item == null) throw new InvalidDataException("تعذر قراءة سجل من قاعدة SQLite."); result.Add(item); } return result;
        }
        private static string Serialize<T>(T value) { return JsonSerializer.Serialize(value, JsonOptions); }
        private static object DbTicks(DateTime? value) { return value.HasValue ? value.Value.Ticks : DBNull.Value; }

        private T InTransaction<T>(Func<T> action, bool checkpoint = false)
        {
            if (transaction != null) return action(); SqliteConnection connection = db ?? throw new ObjectDisposedException(nameof(AppDatabase)); using (SqliteTransaction tx = connection.BeginTransaction()) { transaction = tx; try { T result = action(); tx.Commit(); return result; } catch { tx.Rollback(); throw; } finally { transaction = null; if (checkpoint) Checkpoint(); } }
        }
        private void InTransaction(Action action, bool checkpoint = false) { InTransaction(() => { action(); return true; }, checkpoint); }
        private string MaterializeDatabasePassword() { if (databasePasswordBytes == null) throw new ObjectDisposedException(nameof(AppDatabase)); return Encoding.UTF8.GetString(databasePasswordBytes); }
        public void Close() { if (db != null) { Checkpoint(); db.Dispose(); db = null; } }
        public void Reopen() { if (db == null) Open(); }
        public void Dispose() { Close(); if (databasePasswordBytes != null) { CryptographicOperations.ZeroMemory(databasePasswordBytes); databasePasswordBytes = null; } }
    }

    public sealed class DuplicatePatientException : Exception { public Patient ExistingPatient { get; private set; } public DuplicatePatientException(Patient p) : base("قد يكون المراجع مسجلًا مسبقًا في الملف رقم " + p.FileNumber + ".") { ExistingPatient = p; } }
    public sealed class AppointmentConflictException : Exception { public Appointment ExistingAppointment { get; private set; } public AppointmentConflictException(Appointment a) : base("يوجد موعد متعارض للمراجع " + a.PatientName + "، ملف " + a.FileNumber + ".") { ExistingAppointment = a; } }
}

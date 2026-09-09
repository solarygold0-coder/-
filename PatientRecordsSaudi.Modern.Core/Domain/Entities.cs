using System;
using System.Collections.Generic;
using System.Globalization;

namespace PatientRecordsSaudi.Models
{
    public sealed class Patient
    {
        public Guid Id { get; set; }
        public long FileNumber { get; set; }
        public string IdentityType { get; set; } = string.Empty;
        public string NationalId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Nationality { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public string AlternatePhone { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string EmergencyContact { get; set; } = string.Empty;
        public string EmergencyPhone { get; set; } = string.Empty;
        public string BloodType { get; set; } = string.Empty;
        public string Allergies { get; set; } = string.Empty;
        public string ChronicConditions { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastVisitAt { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string ArchiveReason { get; set; } = string.Empty;
        public string BirthDateText { get { return DateOfBirth.HasValue ? DateOfBirth.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : ""; } }
        public string StatusText { get { return IsArchived ? "مؤرشف" : "نشط"; } }
    }

    public sealed class Appointment
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public long FileNumber { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string VisitType { get; set; } = string.Empty;
        public DateTime StartsAt { get; set; }
        public int DurationMinutes { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReminderNotifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
        public string DateText { get { string[] m = { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" }; return StartsAt.Day.ToString("00") + " - " + StartsAt.Month.ToString("00") + " " + m[StartsAt.Month - 1] + " - " + StartsAt.Year.ToString("0000"); } }
        public string TimeText { get { int h = StartsAt.Hour % 12; if (h == 0) h = 12; return h.ToString("00") + ":" + StartsAt.Minute.ToString("00") + " " + (StartsAt.Hour >= 12 ? "م" : "ص"); } }
    }

    public sealed class PatientTask
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public long FileNumber { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime DueAt { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReminderNotifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
        public string DueText { get { int h = DueAt.Hour % 12; if (h == 0) h = 12; return DueAt.Year.ToString("0000") + "/" + DueAt.Month.ToString("00") + "/" + DueAt.Day.ToString("00") + " " + h.ToString("00") + ":" + DueAt.Minute.ToString("00") + " " + (DueAt.Hour >= 12 ? "م" : "ص"); } }
        public string CompletionText { get { return IsCompleted ? "مكتملة" : "مفتوحة"; } }
    }

    public sealed class AuditEntry
    {
        public Guid Id { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public long? FileNumber { get; set; }
        public string Details { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    public sealed class AppSettings
    {
        public int Id { get; set; }
        public long NextFileNumber { get; set; }
        public int LastInventoryAlertYear { get; set; }
        public bool SecurityNoticeShown { get; set; }
        public string ClinicName { get; set; } = string.Empty;
        public string ClinicPhone { get; set; } = string.Empty;
        public string ClinicAddress { get; set; } = string.Empty;
        public string ClinicLogoStoredId { get; set; } = string.Empty;
        public string ClinicLogoFileName { get; set; } = string.Empty;
        public int DefaultAppointmentMinutes { get; set; }
        public int WorkDayStartMinutes { get; set; }
        public int WorkDayEndMinutes { get; set; }
        public int BackupIntervalHours { get; set; }
        public string AutoBackupDirectory { get; set; } = string.Empty;
        public DateTime? LastAutoBackupAt { get; set; }
        public string LastBackupStatus { get; set; } = string.Empty;
        public List<string> VisitTypes { get; set; } = new();
        public List<string> AppointmentStatuses { get; set; } = new();
        public List<string> TaskPriorities { get; set; } = new();
        public List<string> GenderOptions { get; set; } = new();
        public List<string> BloodTypes { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class PatientAttachment
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public long FileNumber { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string StoredId { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public string UploadedBy { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
        public string SizeText { get { return SizeBytes < 1024 * 1024 ? Math.Max(1, SizeBytes / 1024).ToString("N0") + " ك.ب" : (SizeBytes / 1024d / 1024d).ToString("N1") + " م.ب"; } }
        public string UploadedText { get { return UploadedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture); } }
        public string StatusText { get { return IsDeleted ? "محذوف" : "متاح"; } }
    }

    public sealed class ClosureDate
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}

using System;
using System.Globalization;
using LiteDB;

namespace PatientRecordsSaudi.Models
{
    public sealed class Patient
    {
        [BsonId] public Guid Id { get; set; }
        public long FileNumber { get; set; }
        public string IdentityType { get; set; }
        public string NationalId { get; set; }
        public string FullName { get; set; }
        public string NormalizedName { get; set; }
        public string Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Nationality { get; set; }
        public string Mobile { get; set; }
        public string AlternatePhone { get; set; }
        public string City { get; set; }
        public string Address { get; set; }
        public string EmergencyContact { get; set; }
        public string EmergencyPhone { get; set; }
        public string BloodType { get; set; }
        public string Allergies { get; set; }
        public string ChronicConditions { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastVisitAt { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string ArchiveReason { get; set; }

        [BsonIgnore] public string BirthDateText { get { return DateOfBirth.HasValue ? DateOfBirth.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : ""; } }
        [BsonIgnore] public string StatusText { get { return IsArchived ? "مؤرشف" : "نشط"; } }
    }

    public sealed class Appointment
    {
        [BsonId] public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public long FileNumber { get; set; }
        public string PatientName { get; set; }
        public string Title { get; set; }
        public string VisitType { get; set; }
        public DateTime StartsAt { get; set; }
        public int DurationMinutes { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReminderNotifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; }

        [BsonIgnore] public string DateText { get { string[] m = { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" }; return StartsAt.Day.ToString("00") + " - " + StartsAt.Month.ToString("00") + " " + m[StartsAt.Month - 1] + " - " + StartsAt.Year.ToString("0000"); } }
        [BsonIgnore] public string TimeText { get { int h = StartsAt.Hour % 12; if (h == 0) h = 12; return h.ToString("00") + ":" + StartsAt.Minute.ToString("00") + " " + (StartsAt.Hour >= 12 ? "م" : "ص"); } }
    }

    public sealed class PatientTask
    {
        [BsonId] public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public long FileNumber { get; set; }
        public string PatientName { get; set; }
        public string Title { get; set; }
        public DateTime DueAt { get; set; }
        public string Priority { get; set; }
        public string Notes { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReminderNotifiedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string DeletedBy { get; set; }

        [BsonIgnore] public string DueText { get { int h = DueAt.Hour % 12; if (h == 0) h = 12; return DueAt.Year.ToString("0000") + "/" + DueAt.Month.ToString("00") + "/" + DueAt.Day.ToString("00") + " " + h.ToString("00") + ":" + DueAt.Minute.ToString("00") + " " + (DueAt.Hour >= 12 ? "م" : "ص"); } }
        [BsonIgnore] public string CompletionText { get { return IsCompleted ? "مكتملة" : "مفتوحة"; } }
    }

    public sealed class AuditEntry
    {
        [BsonId] public Guid Id { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Action { get; set; }
        public string EntityType { get; set; }
        public string EntityId { get; set; }
        public long? FileNumber { get; set; }
        public string Details { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }
    }

    public sealed class AppSettings
    {
        [BsonId] public int Id { get; set; }
        public long NextFileNumber { get; set; }
        public int LastInventoryAlertYear { get; set; }
        public string ClinicName { get; set; }
        public string ClinicPhone { get; set; }
        public string ClinicAddress { get; set; }
        public int DefaultAppointmentMinutes { get; set; }
        public int WorkDayStartMinutes { get; set; }
        public int WorkDayEndMinutes { get; set; }
        public int BackupIntervalHours { get; set; }
        public DateTime? LastAutoBackupAt { get; set; }
        public string LastBackupStatus { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ClosureDate
    {
        [BsonId] public Guid Id { get; set; }
        public DateTime Date { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

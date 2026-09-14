using System.Globalization;

namespace SaudiPatientDesk.Domain;

public sealed record Patient(
    long Id,
    string FileNumber,
    string FullName,
    string NationalId,
    string Mobile,
    string? SecondaryContact,
    string? BriefMedicalInfo,
    string Gender,
    string? ResidencyNumber,
    string? BloodType,
    string? ChronicDiseases,
    string? CurrentMedications,
    string? DrugAllergies,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public string UpdatedAtDisplay => UpdatedAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public string GenderDisplay => Gender switch
    {
        "male" => "ذكر",
        "female" => "أنثى",
        _ => Gender
    };
}

public sealed record PatientDraft(
    string FileNumber,
    string FullName,
    string NationalId,
    string Mobile,
    string? SecondaryContact,
    string? BriefMedicalInfo,
    string Gender,
    string? ResidencyNumber,
    string? BloodType,
    string? ChronicDiseases,
    string? CurrentMedications,
    string? DrugAllergies);

public sealed record Appointment(
    long Id,
    long PatientId,
    string FileNumber,
    string PatientName,
    DateTime StartsAt,
    string Status,
    string? Notes,
    long? StaffId = null,
    string? StaffName = null,
    long? DoctorId = null,
    string? DoctorName = null,
    long? SpecialistId = null,
    string? SpecialistName = null,
    long? ClinicId = null,
    string? ClinicName = null,
    string Kind = "regular")
{
    public string KindDisplay => Kind == "renewed" ? "مجدّد" : "عادي";
    public string ClinicianSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(DoctorName)) parts.Add("طبيب: " + DoctorName);
            if (!string.IsNullOrWhiteSpace(SpecialistName)) parts.Add("أخصائي: " + SpecialistName);
            return parts.Count == 0 ? (StaffName ?? "—") : string.Join(" | ", parts);
        }
    }

    public string StartsAtDisplay
    {
        get
        {
            var h = StartsAt.Hour;
            var period = h >= 12 ? "م" : "ص";
            var h12 = h % 12;
            if (h12 == 0) h12 = 12;
            return StartsAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                   + $"  {h12}:{StartsAt.Minute:00} {period}";
        }
    }

    public string StatusDisplay => Status switch
    {
        "scheduled" => "مجدول",
        "completed" => "مكتمل",
        "cancelled" => "ملغي",
        "missed" => "لم يحضر",
        _ => Status
    };
}

public sealed record DashboardSnapshot(
    int ActivePatients,
    int TodayAppointments,
    int UpcomingAlerts,
    int InactiveTenYears);

public sealed record StaffMember(
    long Id,
    string FullName,
    string Role,
    bool IsActive)
{
    public string RoleDisplay => Role switch
    {
        "doctor" => "طبيب",
        "specialist" => "أخصائي معالج",
        _ => Role
    };

    public string Display => $"{FullName} — {RoleDisplay}";
}

public sealed record TimelineSlot(
    DateTime StartsAt,
    string Kind,
    string Label,
    string Caption,
    long? AppointmentId,
    string? PatientName)
{
    public string TimeLabel => ServicesTime(StartsAt);
    private static string ServicesTime(DateTime dt)
    {
        var h = dt.Hour;
        var period = h >= 12 ? "م" : "ص";
        var h12 = h % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12}:{dt.Minute:00} {period}";
    }
}

public sealed record TodayRow(
    Appointment Appointment,
    bool HasAllergy,
    bool HasChronic,
    string? StaffName,
    string? Mobile = null,
    string VisitStage = "")
{
    public string TimeLabel => Appointment.StartsAtDisplay.Contains("  ")
        ? Appointment.StartsAtDisplay.Split(new[] { "  " }, StringSplitOptions.None)[^1]
        : Appointment.StartsAtDisplay;
    public string FlagText =>
        (HasAllergy ? "تحسس  " : "") + (HasChronic ? "مزمن" : "");
}


public sealed class StaffEditRow
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string RoleDisplay { get; set; } = "";
    public bool IsActive { get; set; }
    public string StatusDisplay => IsActive ? "نشط" : "معطّل";
}


public sealed record Clinic(long Id, string Name, bool IsActive);

public sealed class ClinicEditRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public enum AppointmentFilter
{
    Today,
    NextWeek,
    Missed,
    Renewed,
    AfterThreeMonths,
    AllUpcoming
}


public sealed record TeamDayRow(
    long StaffId,
    string StaffName,
    string RoleDisplay,
    int BookedCount,
    string NextFreeLabel);

using System.Globalization;

namespace SaudiPatientDesk.Domain;

public sealed record Patient(
    long Id,
    int FileNumber,
    string FullName,
    string NationalId,
    string Mobile,
    string? SecondaryContact,
    string? BriefMedicalInfo,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public string UpdatedAtDisplay => UpdatedAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
}

public sealed record PatientDraft(
    string FullName,
    string NationalId,
    string Mobile,
    string? SecondaryContact,
    string? BriefMedicalInfo);

public sealed record Appointment(
    long Id,
    long PatientId,
    int FileNumber,
    string PatientName,
    DateTime StartsAt,
    string Status,
    string? Notes)
{
    public string StartsAtDisplay => StartsAt.ToString("yyyy/MM/dd  HH:mm", CultureInfo.InvariantCulture);

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

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
    DateTime UpdatedAt);

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
    string? Notes);

public sealed record DashboardSnapshot(
    int ActivePatients,
    int TodayAppointments,
    int UpcomingAlerts,
    int InactiveTenYears);

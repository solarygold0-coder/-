using System.IO;

namespace SaudiPatientDesk.Data;

public static class AppPaths
{
    // مسار بيانات ثابت ومستقل عن رقم إصدار البرنامج وحزمة MSIX.
    // لا تغيّر هذا المسار عند التحديث حتى لا تتجزأ سجلات المرضى بين الإصدارات.
    public static string Root { get; } = ResolveRoot();

    public static string DatabaseFile => Path.Combine(Root, "clinic-records.sqlite3");
    public static string Attachments => Path.Combine(Root, "Attachments");
    public static string Backups => Path.Combine(Root, "Backups");
    public static string ClinicLogoFile => Path.Combine(Root, "clinic-logo.png");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Attachments);
        Directory.CreateDirectory(Backups);
    }

    private static string ResolveRoot()
    {
        var testRoot = Environment.GetEnvironmentVariable("SAUDI_PATIENT_DESK_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(testRoot))
            return Path.GetFullPath(testRoot);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SaudiPatientDesk", "Clinic2026");
    }
}

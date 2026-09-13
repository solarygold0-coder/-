using System.IO;

namespace SaudiPatientDesk.Data;

public static class AppPaths
{
    // مسار جديد. لا يقرأ Generation6 ولا أي قاعدة تالفة سابقة.
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaudiPatientDesk", "Clinic2026");

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
}

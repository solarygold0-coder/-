namespace SaudiPatientDesk.Data;

public static class AppPaths
{
    // هوية جديدة كلياً؛ لا تقرأ أي مجلد أو قاعدة بيانات من الإصدارات السابقة.
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaudiPatientDesk", "Generation6");

    public static string DatabaseFile => Path.Combine(Root, "patient-records-v6.sqlite3");
    public static string Attachments => Path.Combine(Root, "Attachments");
    public static string Backups => Path.Combine(Root, "Backups");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Attachments);
        Directory.CreateDirectory(Backups);
    }
}

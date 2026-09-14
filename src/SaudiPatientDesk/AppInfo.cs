using System.Reflection;

namespace SaudiPatientDesk;

public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "غير معروف" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}

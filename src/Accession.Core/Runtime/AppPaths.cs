namespace Accession.Core.Runtime;

/// <summary>Per-user file locations (requirements TECH-05).</summary>
public static class AppPaths
{
    public const string AppFolderName = "Accession";

    /// <summary><c>%APPDATA%\Accession\settings.json</c></summary>
    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, "settings.json");

    /// <summary><c>%LOCALAPPDATA%\Accession</c></summary>
    public static string LocalDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary><c>%LOCALAPPDATA%\Accession\logs</c></summary>
    public static string LogFolder => Path.Combine(LocalDataFolder, "logs");

    /// <summary><c>%LOCALAPPDATA%\Accession\cache\dashboard</c>: slow Dashboard sections saved per inventory.</summary>
    public static string DashboardCacheFolder => Path.Combine(LocalDataFolder, "cache", "dashboard");

    /// <summary><c>%LOCALAPPDATA%\Accession\quick copy</c>: Quick Copy manifests (kept out of the flat destination folder).</summary>
    public static string QuickCopyFolder => Path.Combine(LocalDataFolder, "quick copy");
}

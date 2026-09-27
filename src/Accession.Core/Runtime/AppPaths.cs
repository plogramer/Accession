namespace Accession.Core.Runtime;

/// <summary>Per-user file locations (requirements TECH-05).</summary>
public static class AppPaths
{
    public const string AppFolderName = "Accession";

    /// <summary><c>%APPDATA%\Accession\settings.json</c></summary>
    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, "settings.json");

    /// <summary><c>%LOCALAPPDATA%\Accession\logs</c></summary>
    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "logs");
}

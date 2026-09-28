namespace Accession.UI.App;

/// <summary>Help topics: the ids of the sections in the help window (wwwroot/help/index.html).</summary>
public static class HelpTopics
{
    public const string Contents = "contents";
    public const string Start = "start";
    public const string Dashboard = "dashboard";
    public const string Media = "media";
    public const string Files = "files";
    public const string SavedSearches = "saved-searches";
    public const string Categories = "categories";
    public const string ScanQueue = "queue";
    public const string Errors = "errors";
    public const string AuditLog = "audit";
    public const string Export = "export";
    public const string Settings = "settings";
    public const string Locking = "locking";
    public const string Shortcuts = "shortcuts";

    /// <summary>The topic of a side navigation item (its icon name matches the screen's topic id).</summary>
    public static string ForScreen(string? navIcon) => navIcon switch
    {
        Dashboard or Media or Files or Categories or ScanQueue or Errors or AuditLog => navIcon,
        _ => Contents,
    };
}

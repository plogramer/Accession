using Accession.Core.Inventories;

namespace Accession.Data.MediaManagement;

/// <summary>Rules for which folders under the root count as media.</summary>
public static class MediaFolders
{
    /// <summary>System folders ignored at root level only (DSC-05). Inside media everything is scanned.</summary>
    public static readonly IReadOnlySet<string> IgnoredAtRoot =
        new HashSet<string>(["$RECYCLE.BIN", "System Volume Information"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Relative path of a media folder: <c>\&lt;MediaID&gt;\</c>.</summary>
    public static string RelativePath(string mediaId) => $@"\{mediaId}\";

    /// <summary>Immediate subfolders of the root (names as on disk), excluding ignored system folders.</summary>
    public static IReadOnlyList<string> ListCandidateNames(string rootPath) =>
        Directory.EnumerateDirectories(rootPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !IgnoredAtRoot.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Full path of a media folder under the root.</summary>
    public static string FullPath(string rootPath, string mediaId) => Path.Combine(PathRules.NormalizeDirectory(rootPath), mediaId);
}

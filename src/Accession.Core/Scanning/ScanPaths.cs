namespace Accession.Core.Scanning;

/// <summary>
/// Path rules for stored data (SCN-13, SCN-14): relative paths start with <c>\&lt;MediaID&gt;\</c>, use backslashes
/// and end with <c>\</c> for folders; extensions are lowercase without the dot.
/// </summary>
public static class ScanPaths
{
    /// <summary>Text after the last '.', lowercase, without the dot; "" if none (same rule as Path.GetExtension).</summary>
    public static string Extension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length <= 1 ? string.Empty : extension[1..].ToLowerInvariant();
    }

    /// <summary>Relative path of a media folder: <c>\MediaID\</c>.</summary>
    public static string MediaFolder(string mediaId) => $@"\{mediaId}\";

    /// <summary>Relative path of a child folder: <c>parent + name + \</c>.</summary>
    public static string ChildFolder(string parentRelativePath, string name) => parentRelativePath + name + @"\";

    /// <summary>Relative path of a file in a folder.</summary>
    public static string File(string folderRelativePath, string name) => folderRelativePath + name;

    /// <summary>Full path of a relative path under the root, using the OS separator.</summary>
    public static string ToFullPath(string rootPath, string relativePath)
    {
        var parts = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([rootPath, .. parts]);
    }
}

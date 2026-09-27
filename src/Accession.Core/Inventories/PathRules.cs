namespace Accession.Core.Inventories;

/// <summary>Path comparisons with Windows semantics (case-insensitive).</summary>
public static class PathRules
{
    public static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>Full path without a trailing separator (except for roots like <c>C:\</c>).</summary>
    public static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    public static bool AreSameDirectory(string a, string b) =>
        string.Equals(NormalizeDirectory(a), NormalizeDirectory(b), Comparison);

    /// <summary>True when <paramref name="path"/> is strictly below <paramref name="directory"/>.</summary>
    public static bool IsUnder(string path, string directory)
    {
        var parent = NormalizeDirectory(directory);
        var child = Path.GetFullPath(path);
        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return child.Length > prefix.Length && child.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// True when <paramref name="filePath"/> lies inside a media folder, i.e. inside any subfolder of
    /// <paramref name="rootFolder"/>. Files directly in the root are allowed (INV-03).
    /// </summary>
    public static bool IsInsideMediaFolder(string filePath, string rootFolder)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return directory is not null && IsUnder(directory, rootFolder);
    }
}

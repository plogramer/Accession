namespace Accession.Core.Copying;

/// <summary>Destination paths and the "never write under the root" guard (SCN-40, CPY-04).</summary>
public static class CopyPaths
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Joins a destination (or root) with an inventory relative path (<c>\Media\folder\name</c>).</summary>
    public static string Combine(string basePath, string relativePath)
    {
        var parts = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([basePath, .. parts]);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        var p = Normalize(path);
        var f = Normalize(folder);
        return p.Equals(f, PathComparison) || p.StartsWith(f + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>Why <paramref name="destination"/> cannot be used; null when it can.</summary>
    public static string? ValidateDestination(string? destination, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return "Choose a destination folder.";
        }

        if (!Path.IsPathFullyQualified(destination))
        {
            return "The destination must be a full path, such as D:\\Production\\Copy1.";
        }

        if (!string.IsNullOrWhiteSpace(rootPath) && IsSameOrInside(destination, rootPath))
        {
            return "The destination cannot be the root folder or a folder inside it: nothing is ever written under the root.";
        }

        return null;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

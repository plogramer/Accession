using System.IO.Enumeration;

namespace Accession.Core.Scanning;

/// <summary>
/// <see cref="IDirectoryLister"/> over <see cref="FileSystemEnumerable{TResult}"/>. On Windows this reads the
/// directory with NtQueryDirectoryFile (size and timestamps come from the listing; files are not opened) and
/// handles paths longer than 260 characters automatically.
/// </summary>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0, // hidden and system items are evidence too
    };

    public IReadOnlyList<DirectoryEntry> List(string fullPath)
    {
        var enumerable = new FileSystemEnumerable<DirectoryEntry>(fullPath, ToEntry, Options);
        return enumerable.ToList();
    }

    public DirectoryEntry GetDirectory(string fullPath)
    {
        var info = new DirectoryInfo(fullPath);
        if (!info.Exists)
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{fullPath}'.");
        }

        return new DirectoryEntry(
            info.Name,
            IsDirectory: true,
            Size: 0,
            new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastAccessTimeUtc, TimeSpan.Zero),
            (info.Attributes & FileAttributes.ReparsePoint) != 0);
    }

    private static DirectoryEntry ToEntry(ref FileSystemEntry entry) => new(
        entry.FileName.ToString(),
        entry.IsDirectory,
        entry.IsDirectory ? 0 : entry.Length,
        entry.CreationTimeUtc,
        entry.LastWriteTimeUtc,
        entry.LastAccessTimeUtc,
        (entry.Attributes & FileAttributes.ReparsePoint) != 0);
}

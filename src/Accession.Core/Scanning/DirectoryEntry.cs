namespace Accession.Core.Scanning;

/// <summary>One entry of a directory listing: metadata read from the listing, without opening the item.</summary>
public sealed record DirectoryEntry(
    string Name,
    bool IsDirectory,
    long Size,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    DateTimeOffset AccessedUtc,
    bool IsReparsePoint);

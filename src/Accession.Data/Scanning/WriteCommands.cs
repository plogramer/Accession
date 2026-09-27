using Accession.Core.Model;

namespace Accession.Data.Scanning;

public sealed record FolderRow(
    long FolderId,
    long MediaKey,
    long? ParentFolderId,
    string Name,
    string RelativePath,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? AccessedUtc,
    bool IsReparsePoint,
    bool IsEnumerated);

public sealed record FileRow(
    long FileId,
    long MediaKey,
    long FolderId,
    string Name,
    string Extension,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset? AccessedUtc);

/// <summary>A command for <see cref="ScanDbWriter"/>. Each command is applied inside one transaction.</summary>
public abstract record WriteCommand;

/// <summary>Inserts one folder (the media folder at the start of a full scan).</summary>
public sealed record InsertFolderCommand(FolderRow Folder) : WriteCommand;

/// <summary>
/// The result of listing one folder: its child folders and files are inserted and the folder is marked
/// <c>IsEnumerated = 1</c> in the same transaction, so a folder is either fully recorded or not at all (SCN-30).
/// </summary>
public sealed record FolderListingCommand(long FolderId, IReadOnlyList<FolderRow> ChildFolders, IReadOnlyList<FileRow> Files) : WriteCommand;

public sealed record HashResultCommand(long FileId, string? Sha1, HashStatus Status, DateTimeOffset? HashedAtUtc) : WriteCommand;

public sealed record ErrorCommand(ScanErrorEntry Error) : WriteCommand;

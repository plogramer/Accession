using Accession.Core.Model;

namespace Accession.Data.Browsing;

/// <summary>A folder in the File browser tree.</summary>
public sealed class FolderNode
{
    public long FolderId { get; init; }
    public long MediaKey { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public bool HasChildren { get; init; }
    public bool IsReparsePoint { get; init; }
}

public enum FileSortColumn
{
    /// <summary>Folder order, then file order (FileId). Fastest.</summary>
    Default,
    Name,
    Extension,
    Size,
    Modified,
}

/// <summary>Filters of the File browser (requirement BRW-03). All set filters are combined with AND.</summary>
public sealed record FileFilter
{
    public long? MediaKey { get; init; }

    /// <summary>Limit to these media (e.g. the Dashboard's media selection). An empty set matches no files.</summary>
    public IReadOnlyCollection<long>? MediaKeys { get; init; }

    /// <summary>Limit to this folder (and its subfolders when <see cref="IncludeSubfolders"/>).</summary>
    public long? FolderId { get; init; }

    public bool IncludeSubfolders { get; init; } = true;
    public int? CategoryId { get; init; }

    /// <summary>Exact extension (lowercase, no dot); "" = files without extension.</summary>
    public string? Extension { get; init; }

    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }

    /// <summary>Inclusive lower bound (UTC).</summary>
    public DateTimeOffset? ModifiedFrom { get; init; }

    /// <summary>Exclusive upper bound (UTC).</summary>
    public DateTimeOffset? ModifiedTo { get; init; }

    public HashStatus? HashStatus { get; init; }
    public bool DuplicatesOnly { get; init; }

    /// <summary>Files that could not be hashed or have a file error row.</summary>
    public bool ErrorsOnly { get; init; }

    public string? NameContains { get; init; }
    public string? Sha1 { get; init; }

    public static readonly FileFilter None = new();
}

/// <summary>One row of the File browser grid.</summary>
public sealed class FileItem
{
    public long FileId { get; init; }
    public long MediaKey { get; init; }
    public string MediaId { get; init; } = string.Empty;
    public long FolderId { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? ModifiedUtc { get; init; }
    public DateTimeOffset? AccessedUtc { get; init; }
    public string? Sha1 { get; init; }
    public HashStatus HashStatus { get; init; }

    /// <summary>Number of files in the inventory with the same SHA-1 (1 = unique; 0 = not hashed).</summary>
    public long DuplicateCount { get; init; }

    public string RelativePath => FolderPath + Name;
}

/// <summary>Keyset position after the last row of a page.</summary>
public sealed record FilePageCursor(object? SortValue, long FileId);

public sealed record FilePage(IReadOnlyList<FileItem> Items, FilePageCursor? Next);

public sealed class FileTotals
{
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
}

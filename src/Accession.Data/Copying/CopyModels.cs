using Accession.Core.Copying;
using Accession.Data.Browsing;

namespace Accession.Data.Copying;

/// <summary>Which files to copy and where (requirements 5.8b).</summary>
/// <param name="Filter">The ticked rows (a <see cref="FileFilter.FileIds"/> filter) or the Files screen's current filter.</param>
/// <param name="Destination">Folder the copies go to. Never the root or inside it.</param>
/// <param name="ManifestPath">The CSV manifest listing every file.</param>
public sealed record CopyRequest(FileFilter Filter, string Destination, CopyNaming Naming, string ManifestPath)
{
    /// <summary>"12 ticked files", "All results: Media MED001": recorded in the audit entry.</summary>
    public string? ScopeText { get; init; }
}

public sealed record CopyProgress(long FilesDone, long TotalFiles, long BytesDone, long TotalBytes, long Copied, long Skipped, long Failed);

public sealed record CopyBatchResult(string BatchPath, string ManifestPath, long Files, long Bytes)
{
    /// <summary>Files left out because they have no SHA-1 yet (SHA-1 names only); listed in the manifest.</summary>
    public long NotInBatch { get; init; }
}

public sealed record CopyFilesResult(string Destination, string ManifestPath, long TotalFiles, long Copied, long Verified, long Skipped, long Failed,
    long BytesCopied, bool Cancelled, long MetadataWarnings)
{
    /// <summary>Files not reached because the copy was cancelled.</summary>
    public long NotReached => TotalFiles - Copied - Skipped - Failed;
}

/// <summary>One file to copy, in copy order.</summary>
internal sealed class CopySourceRow
{
    public long FileId { get; init; }
    public string MediaId { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? ModifiedUtc { get; init; }
    public string? Sha1 { get; init; }
}

namespace Accession.Core.Model;

/// <summary>A media folder registered in the inventory (one row of the <c>Media</c> table).</summary>
public sealed class Media
{
    public long MediaKey { get; set; }

    /// <summary>The business "Media ID": the folder name as it appears on disk.</summary>
    public string MediaId { get; set; } = string.Empty;

    /// <summary>Path relative to the root, e.g. <c>\123-123_001\</c>.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public MediaStatus Status { get; set; }

    /// <summary>Status to restore when a Missing folder reappears.</summary>
    public MediaStatus? StatusBeforeMissing { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
    public string AddedBy { get; set; } = string.Empty;

    /// <summary>Number of full scans (resumes are not counted).</summary>
    public int ScanCount { get; set; }

    public DateTimeOffset? LastScanStartedUtc { get; set; }
    public DateTimeOffset? LastScanCompletedUtc { get; set; }

    public long FolderCount { get; set; }
    public long FileCount { get; set; }
    public long TotalBytes { get; set; }
    public long HashedCount { get; set; }
    public long ErrorCount { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}

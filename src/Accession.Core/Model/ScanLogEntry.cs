namespace Accession.Core.Model;

public enum ScanType
{
    Full,
    Resume,
    RetryFailed,
}

public enum ScanOutcome
{
    Completed,
    CompletedWithErrors,
    Cancelled,
    Paused,
    Failed,
    Interrupted,
}

/// <summary>One scan run (one row of <c>ScanLog</c>).</summary>
public sealed class ScanLogEntry
{
    public long ScanId { get; set; }
    public long MediaKey { get; set; }

    /// <summary>Copy of the Media ID, kept even if the media is later deleted.</summary>
    public string MediaId { get; set; } = string.Empty;

    public ScanType ScanType { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public ScanOutcome? Outcome { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public int EnumThreads { get; set; }
    public int HashThreads { get; set; }
    public long? FolderCount { get; set; }
    public long? FileCount { get; set; }
    public long? TotalBytes { get; set; }
    public long? HashedCount { get; set; }
    public long? ErrorCount { get; set; }
    public string? Notes { get; set; }
}

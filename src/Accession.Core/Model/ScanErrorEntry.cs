namespace Accession.Core.Model;

public enum ScanItemType
{
    Folder,
    File,
}

/// <summary>Error types from requirement SCN-51.</summary>
public enum ScanErrorType
{
    AccessDenied,
    FileLocked,
    NotFound,
    PathError,
    IOError,
    ChangedDuringScan,
    ReparsePointSkipped,
    Other,
}

public enum ScanErrorSeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>One row of <c>ScanError</c>.</summary>
public sealed class ScanErrorEntry
{
    public long ErrorId { get; set; }
    public long MediaKey { get; set; }
    public long? ScanId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public ScanItemType ItemType { get; set; }
    public ScanErrorType ErrorType { get; set; }
    public ScanErrorSeverity Severity { get; set; } = ScanErrorSeverity.Error;

    /// <summary>Win32 error code or HRESULT, when known.</summary>
    public int? ErrorCode { get; set; }

    public string? Message { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

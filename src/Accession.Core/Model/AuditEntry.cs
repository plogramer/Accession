namespace Accession.Core.Model;

/// <summary>Audited actions (requirement AUD-02). Stored as text.</summary>
public enum AuditAction
{
    InventoryCreated,
    InventoryOpened,
    InventoryOpenedReadOnly,
    InventoryClosed,
    LockAcquired,
    LockReleased,
    LockForced,

    /// <summary>The lock left by the same user's earlier session on the same computer (not closed properly) was taken back.</summary>
    LockRecovered,
    SchemaUpgraded,
    ConfigUpdated,
    RootPathChanged,
    MediaAdded,
    MediaDeleted,
    ScanQueued,
    ScanStarted,
    ScanPaused,
    ScanResumed,
    ScanCancelled,
    ScanCompleted,
    ScanFailed,
    ExportCreated,
    SavedSearchCreated,
    SavedSearchChanged,
    SavedSearchDeleted,
    SavedSearchFilesAdded,
    SavedSearchFilesRemoved,

    /// <summary>A copy batch (.bat) and its manifest were written (CPY-05).</summary>
    CopyBatchGenerated,

    /// <summary>Files were copied out of the evidence by the app (CPY-06).</summary>
    FilesCopied,
}

/// <summary>One row of <c>AuditLog</c>. Audit rows are never updated or deleted.</summary>
public sealed class AuditEntry
{
    public long AuditId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary><c>DOMAIN\user</c></summary>
    public string UserName { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? MediaId { get; set; }

    /// <summary>Action-specific details as JSON, or null.</summary>
    public string? Details { get; set; }
}

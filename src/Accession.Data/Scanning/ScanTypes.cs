using Accession.Core.Model;

namespace Accession.Data.Scanning;

/// <summary>Thread and batch settings for a scan run (from user settings).</summary>
public sealed record ScanOptions(int EnumerationThreads, int HashingThreads, int DbBatchSize)
{
    public static readonly ScanOptions Default = new(4, 4, 10_000);
}

public enum CoordinatorState
{
    Idle,
    Running,
    Paused,
}

/// <summary>A media waiting in (or running from) the scan queue.</summary>
public sealed record ScanQueueItem(long MediaKey, string MediaId, ScanType Type, MediaStatus PreviousStatus);

public sealed class MediaStatusChangedEventArgs(long mediaKey, MediaStatus status) : EventArgs
{
    public long MediaKey { get; } = mediaKey;
    public MediaStatus Status { get; } = status;
}

public sealed class ScanFinishedEventArgs(ScanQueueItem item, ScanOutcome outcome, ScanTotals totals, string? message) : EventArgs
{
    public ScanQueueItem Item { get; } = item;
    public ScanOutcome Outcome { get; } = outcome;
    public ScanTotals Totals { get; } = totals;

    /// <summary>Failure reason, when <see cref="Outcome"/> is Failed.</summary>
    public string? Message { get; } = message;
}

/// <summary>A media that could not be queued, and why.</summary>
public sealed record EnqueueProblem(long MediaKey, string MediaId, string Reason);

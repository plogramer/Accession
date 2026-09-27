namespace Accession.Core.Scanning;

public enum ScanPhase
{
    Starting,
    Enumerating,
    Hashing,
    Finalizing,
}

/// <summary>A point-in-time view of a running scan for the UI (requirement SCN-24).</summary>
public sealed record ScanProgressSnapshot(
    long MediaKey,
    string MediaId,
    ScanPhase Phase,
    bool IsPaused,
    long FoldersFound,
    long FilesFound,
    long BytesFound,
    long FilesHashed,
    long BytesHashed,
    long Errors,
    double FilesPerSecond,
    double BytesPerSecond,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    string? CurrentPath,
    bool EnumerationDone)
{
    /// <summary>Hashing progress by bytes, 0–100.</summary>
    public double PercentByBytes =>
        BytesFound > 0 ? Math.Min(100d, 100d * BytesHashed / BytesFound)
        : EnumerationDone && FilesHashed >= FilesFound ? 100d
        : 0d;
}

/// <summary>
/// Turns <see cref="ScanCounters"/> into snapshots with throughput averaged over a moving window and an ETA
/// (only once enumeration has finished).
/// </summary>
public sealed class ScanProgressTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly long _started;
    private readonly Queue<(long Timestamp, long Files, long Bytes)> _samples = new();

    public ScanProgressTracker(TimeProvider timeProvider, TimeSpan? window = null)
    {
        _timeProvider = timeProvider;
        _window = window ?? TimeSpan.FromSeconds(10);
        _started = timeProvider.GetTimestamp();
    }

    public ScanProgressSnapshot Sample(long mediaKey, string mediaId, ScanPhase phase, bool isPaused, ScanCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        var now = _timeProvider.GetTimestamp();
        var files = counters.FilesHashed;
        var bytes = counters.BytesHashed;
        _samples.Enqueue((now, files, bytes));
        while (_samples.Count > 2 && _timeProvider.GetElapsedTime(_samples.Peek().Timestamp, now) > _window)
        {
            _samples.Dequeue();
        }

        var oldest = _samples.Peek();
        var seconds = _timeProvider.GetElapsedTime(oldest.Timestamp, now).TotalSeconds;
        var filesPerSecond = isPaused || seconds <= 0 ? 0 : (files - oldest.Files) / seconds;
        var bytesPerSecond = isPaused || seconds <= 0 ? 0 : (bytes - oldest.Bytes) / seconds;

        TimeSpan? eta = null;
        var remaining = counters.BytesFound - bytes;
        if (counters.EnumerationDone && !isPaused)
        {
            if (remaining <= 0)
            {
                eta = TimeSpan.Zero;
            }
            else if (bytesPerSecond > 0)
            {
                eta = TimeSpan.FromSeconds(Math.Min(remaining / bytesPerSecond, TimeSpan.MaxValue.TotalSeconds / 2));
            }
        }

        return new ScanProgressSnapshot(
            mediaKey, mediaId, phase, isPaused,
            counters.FoldersFound, counters.FilesFound, counters.BytesFound, files, bytes, counters.Errors,
            filesPerSecond, bytesPerSecond, _timeProvider.GetElapsedTime(_started, now), eta,
            counters.CurrentPath, counters.EnumerationDone);
    }
}

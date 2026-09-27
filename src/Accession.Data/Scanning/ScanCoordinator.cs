using System.Threading.Channels;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Scanning;
using Accession.Data.Repositories;
using Accession.Data.Sessions;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Scanning;

/// <summary>
/// Runs the scan queue for one open inventory (requirements 5.5): one media at a time (FIFO), multi-threaded
/// within a media; full scan, resume and retry-failed runs; pause, resume and cancel; automatic pause when the
/// network is lost; ScanLog, audit, summaries and media totals.
/// </summary>
public sealed class ScanCoordinator : IAsyncDisposable
{
    private const int HashQueueCapacity = 10_000;
    private const int PendingPageSize = 5_000;

    private readonly InventorySession _session;
    private readonly IDirectoryLister _lister;
    private readonly IFileHasher _hasher;
    private readonly Func<ScanOptions> _options;
    private readonly IAppInfo _appInfo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScanCoordinator> _logger;
    private readonly IReadOnlyList<TimeSpan>? _retryDelays;
    private readonly Lock _gate = new();

    // Serializes status transitions of the running media so e.g. "Hashing" never overwrites "Paused".
    private readonly Lock _statusGate = new();
    private readonly List<ScanQueueItem> _waiting = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;

    // State of the running scan (guarded by _gate where shared with the UI thread).
    private ScanQueueItem? _current;
    private CancellationTokenSource? _runCancel;
    private PauseGate? _pause;
    private ScanCounters? _counters;
    private ScanOutcome _cancelOutcome = ScanOutcome.Cancelled;

    // Set (under _statusGate) once the running scan starts writing its final status; Pause/Resume then do nothing.
    private bool _finishing;
    private readonly List<string> _notes = [];

    public ScanCoordinator(
        InventorySession session,
        IDirectoryLister lister,
        IFileHasher hasher,
        Func<ScanOptions> options,
        IAppInfo appInfo,
        TimeProvider timeProvider,
        ILogger<ScanCoordinator> logger,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _session = session;
        _lister = lister;
        _hasher = hasher;
        _options = options;
        _appInfo = appInfo;
        _timeProvider = timeProvider;
        _logger = logger;
        _retryDelays = retryDelays;
        _loop = Task.Run(RunLoopAsync);
    }

    /// <summary>Queue, current item or state changed.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Progress of the running scan, about once per second (from a background thread).</summary>
    public event EventHandler<ScanProgressSnapshot>? ProgressChanged;

    public event EventHandler<MediaStatusChangedEventArgs>? MediaStatusChanged;

    public event EventHandler<ScanFinishedEventArgs>? ScanFinished;

    /// <summary>The scan paused itself because the share or network is unavailable (SCN-07).</summary>
    public event EventHandler<string>? AutoPaused;

    public CoordinatorState State
    {
        get
        {
            lock (_gate)
            {
                return _current is null ? CoordinatorState.Idle : _pause?.IsPaused == true ? CoordinatorState.Paused : CoordinatorState.Running;
            }
        }
    }

    public ScanQueueItem? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public IReadOnlyList<ScanQueueItem> Waiting
    {
        get
        {
            lock (_gate)
            {
                return [.. _waiting];
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _current is not null || _waiting.Count > 0;
            }
        }
    }

    /// <summary>Adds a media to the queue (SCN-01). Throws if the media cannot be scanned in its current state.</summary>
    public void Enqueue(long mediaKey, ScanType type)
    {
        _session.EnsureWritable();
        if (!_session.IsRootAvailable)
        {
            throw new InvalidOperationException("The root folder is not available.");
        }

        using var scope = _session.Database.Open();
        var repo = new MediaRepository(scope);
        var media = repo.Get(mediaKey) ?? throw new InvalidOperationException("The media does not exist.");
        lock (_gate)
        {
            if (_current?.MediaKey == mediaKey || _waiting.Any(w => w.MediaKey == mediaKey))
            {
                throw new InvalidOperationException($"'{media.MediaId}' is already in the scan queue.");
            }
        }

        var reason = CannotScanReason(media, type);
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }

        using (var transaction = scope.BeginTransaction())
        {
            repo.SetStatus(mediaKey, MediaStatus.Queued);
            _session.Audit.Write(scope, AuditAction.ScanQueued, media.MediaId, new { ScanType = type.ToString() });
            transaction.Commit();
        }

        lock (_gate)
        {
            _waiting.Add(new ScanQueueItem(mediaKey, media.MediaId, type, media.Status));
        }

        RaiseStatus(mediaKey, MediaStatus.Queued);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _signal.Release();
    }

    /// <summary>Why a media cannot be scanned with <paramref name="type"/>, or null if it can.</summary>
    public static string? CannotScanReason(Media media, ScanType type)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (media.IsDeleted)
        {
            return $"'{media.MediaId}' was deleted.";
        }

        return (type, media.Status) switch
        {
            (_, MediaStatus.Missing) => $"The folder of '{media.MediaId}' is missing from the root folder.",
            (_, MediaStatus.Queued or MediaStatus.Scanning or MediaStatus.Hashing or MediaStatus.Paused) => $"'{media.MediaId}' is already being scanned.",
            (ScanType.Resume, not MediaStatus.Incomplete) => $"'{media.MediaId}' has no incomplete scan to resume.",
            (ScanType.RetryFailed, MediaStatus.New) => $"'{media.MediaId}' has not been scanned yet.",
            _ => null,
        };
    }

    /// <summary>Removes a waiting media from the queue and restores its previous status.</summary>
    public bool Remove(long mediaKey)
    {
        ScanQueueItem? item;
        lock (_gate)
        {
            item = _waiting.FirstOrDefault(w => w.MediaKey == mediaKey);
            if (item is null)
            {
                return false;
            }

            _waiting.Remove(item);
        }

        SetStatus(item.MediaKey, item.PreviousStatus);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void MoveUp(long mediaKey) => Move(mediaKey, -1);

    public void MoveDown(long mediaKey) => Move(mediaKey, +1);

    /// <summary>Pauses the running scan: no new work starts, in-flight files finish (SCN-05).</summary>
    public void Pause() => PauseCore("user", null);

    public void Resume()
    {
        ScanQueueItem? current;
        PauseGate? pause;
        ScanCounters? counters;
        lock (_gate)
        {
            current = _current;
            pause = _pause;
            counters = _counters;
            if (current is null || pause is not { IsPaused: true })
            {
                return;
            }
        }

        lock (_statusGate)
        {
            if (_finishing)
            {
                pause.Resume();
                return;
            }

            var status = counters?.EnumerationDone == true || current.Type == ScanType.RetryFailed ? MediaStatus.Hashing : MediaStatus.Scanning;
            SetStatus(current.MediaKey, status, AuditAction.ScanResumed);
            pause.Resume();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels the running scan; the media becomes Incomplete and can be resumed (SCN-05).</summary>
    public void Cancel() => CancelCurrent(ScanOutcome.Cancelled);

    /// <summary>
    /// For closing the inventory or the app: clears the queue (restoring statuses), interrupts the running scan
    /// (outcome Interrupted, media Incomplete) and waits for it to stop.
    /// </summary>
    public async Task StopAsync()
    {
        List<ScanQueueItem> waiting;
        lock (_gate)
        {
            waiting = [.. _waiting];
            _waiting.Clear();
        }

        foreach (var item in waiting)
        {
            SetStatus(item.MediaKey, item.PreviousStatus);
        }

        CancelCurrent(ScanOutcome.Interrupted);
        while (Current is not null)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
        _signal.Dispose();
    }

    private void Move(long mediaKey, int delta)
    {
        lock (_gate)
        {
            var index = _waiting.FindIndex(w => w.MediaKey == mediaKey);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _waiting.Count)
            {
                return;
            }

            (_waiting[index], _waiting[target]) = (_waiting[target], _waiting[index]);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PauseCore(string reason, string? detail)
    {
        ScanQueueItem? current;
        lock (_statusGate)
        {
            lock (_gate)
            {
                current = _current;
                if (current is null || _pause is null || _pause.IsPaused || _finishing)
                {
                    return;
                }

                _pause.Pause();
            }

            SetStatus(current.MediaKey, MediaStatus.Paused, AuditAction.ScanPaused, new { Reason = reason, Detail = detail });
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelCurrent(ScanOutcome outcome)
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            _cancelOutcome = outcome;
            _runCancel?.Cancel();
            _pause?.Resume(); // let paused workers observe the cancellation
        }
    }

    private async Task RunLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            ScanQueueItem? next;
            lock (_gate)
            {
                next = _waiting.FirstOrDefault();
                if (next is null)
                {
                    continue;
                }

                _waiting.RemoveAt(0);
                _current = next;
                _runCancel = new CancellationTokenSource();
                _pause = new PauseGate();
                _counters = new ScanCounters();
                _cancelOutcome = ScanOutcome.Cancelled;
                _notes.Clear();
            }

            lock (_statusGate)
            {
                _finishing = false;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await RunOneAsync(next, _runCancel.Token, _pause, _counters).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // RunOneAsync records failures itself; this only guards the loop.
                _logger.LogError(ex, "Unexpected error in the scan loop for {MediaId}", next.MediaId);
            }
            finally
            {
                lock (_gate)
                {
                    _current = null;
                    _runCancel.Dispose();
                    _runCancel = null;
                    _pause = null;
                    _counters = null;
                }

                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task RunOneAsync(ScanQueueItem item, CancellationToken cancellationToken, PauseGate pause, ScanCounters counters)
    {
        var options = _options();
        var root = _session.Config.RootPath;
        var mediaFolder = Path.Combine(root, item.MediaId);
        var started = _timeProvider.GetUtcNow();
        long scanId;
        ScanIdAllocator ids;
        var startFolders = new List<FolderWork>();
        FolderRow? rootRow = null;

        // ---- Start: validate, reset data for a full scan, ScanLog + audit ----
        using (var scope = _session.Database.Open())
        {
            if (!Directory.Exists(mediaFolder))
            {
                using var tx = scope.BeginTransaction();
                new MediaRepository(scope).MarkMissing(item.MediaKey);
                _session.Audit.Write(scope, AuditAction.ScanFailed, item.MediaId, new { Reason = "Media folder not found", Path = mediaFolder });
                tx.Commit();
                RaiseStatus(item.MediaKey, MediaStatus.Missing);
                ScanFinished?.Invoke(this, new ScanFinishedEventArgs(item, ScanOutcome.Failed, ScanTotals.Zero, "The media folder was not found."));
                return;
            }

            using (var tx = scope.BeginTransaction())
            {
                var media = new MediaRepository(scope);
                if (item.Type == ScanType.Full)
                {
                    new ScanDataRepository(scope).DeleteForMedia(item.MediaKey);
                }
                else if (item.Type == ScanType.RetryFailed)
                {
                    scope.Connection.Execute(
                        """
                        UPDATE File SET HashStatus = 0, Sha1 = NULL, HashedAtUtc = NULL WHERE MediaKey = @MediaKey AND HashStatus = 2;
                        DELETE FROM ScanError WHERE MediaKey = @MediaKey AND ItemType = 'File'
                            AND ErrorType NOT IN ('ReparsePointSkipped', 'ChangedDuringScan');
                        """,
                        new { item.MediaKey }, scope.Transaction);
                }
                else
                {
                    // Resume: folders that were never listed are listed again; drop any partial rows (SCN-30).
                    scope.Connection.Execute(
                        "DELETE FROM File WHERE FolderId IN (SELECT FolderId FROM Folder WHERE MediaKey = @MediaKey AND IsEnumerated = 0)",
                        new { item.MediaKey }, scope.Transaction);
                    startFolders.AddRange(scope.Connection.Query<FolderWork>(
                        "SELECT FolderId, RelativePath FROM Folder WHERE MediaKey = @MediaKey AND IsEnumerated = 0 ORDER BY FolderId",
                        new { item.MediaKey }, scope.Transaction));
                }

                media.MarkScanStarted(item.MediaKey, started, isFullScan: item.Type == ScanType.Full);
                scanId = new ScanLogRepository(scope).Start(new ScanLogEntry
                {
                    MediaKey = item.MediaKey,
                    MediaId = item.MediaId,
                    ScanType = item.Type,
                    StartedAtUtc = started,
                    UserName = _session.UserName,
                    MachineName = Environment.MachineName,
                    AppVersion = _appInfo.Version,
                    EnumThreads = options.EnumerationThreads,
                    HashThreads = options.HashingThreads,
                });
                var status = item.Type == ScanType.RetryFailed ? MediaStatus.Hashing : MediaStatus.Scanning;
                media.SetStatus(item.MediaKey, status);
                _session.Audit.Write(scope, AuditAction.ScanStarted, item.MediaId, new { ScanId = scanId, ScanType = item.Type.ToString(), options.EnumerationThreads, options.HashingThreads });
                tx.Commit();
                RaiseStatus(item.MediaKey, status);
            }

            ids = ScanIdAllocator.Create(scope);
        }

        if (item.Type == ScanType.Full)
        {
            var entry = _lister.GetDirectory(mediaFolder);
            var rootId = ids.NextFolderId();
            var relative = ScanPaths.MediaFolder(item.MediaId);
            rootRow = new FolderRow(rootId, item.MediaKey, null, item.MediaId, relative,
                entry.CreatedUtc, entry.ModifiedUtc, entry.AccessedUtc, entry.IsReparsePoint, IsEnumerated: false);
            startFolders.Add(new FolderWork(rootId, relative));
        }

        // ---- Run ----
        await using var writer = new ScanDbWriter(_session.Database, options.DbBatchSize, _timeProvider);
        var retry = new NetworkRetry(_timeProvider, OnNetworkUnavailableAsync, _retryDelays);
        var context = new ScanContext
        {
            MediaKey = item.MediaKey,
            MediaId = item.MediaId,
            ScanId = scanId,
            RootPath = root,
            Writer = writer,
            Ids = ids,
            Counters = counters,
            Pause = pause,
            Retry = retry,
            TimeProvider = _timeProvider,
        };
        var tracker = new ScanProgressTracker(_timeProvider);
        using var progressTimer = _timeProvider.CreateTimer(_ => PublishProgress(item, tracker, counters, pause), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            if (rootRow is not null)
            {
                await writer.WriteAsync(new InsertFolderCommand(rootRow), failure.Token).ConfigureAwait(false);
            }

            var hashQueue = Channel.CreateBounded<HashWork>(new BoundedChannelOptions(HashQueueCapacity) { FullMode = BoundedChannelFullMode.Wait });
            var enumeration = RunGuardedAsync(async () =>
            {
                await new EnumerationPhase(_lister).RunAsync(context, startFolders, options.EnumerationThreads, hashQueue.Writer, failure.Token).ConfigureAwait(false);
                lock (_statusGate)
                {
                    counters.EnumerationDone = true;
                    if (item.Type != ScanType.RetryFailed && !pause.IsPaused && !failure.IsCancellationRequested)
                    {
                        SetStatus(item.MediaKey, MediaStatus.Hashing);
                    }
                }
            }, failure);
            var pendingFiles = item.Type == ScanType.Full
                ? Task.CompletedTask
                : RunGuardedAsync(() => LoadPendingFilesAsync(item.MediaKey, ids.InitialLastFileId, counters, hashQueue.Writer, failure.Token), failure);
            var producers = Task.WhenAll(enumeration, pendingFiles).ContinueWith(t => hashQueue.Writer.TryComplete(t.Exception), TaskScheduler.Default);
            var hashing = RunGuardedAsync(() => new HashingPhase(_hasher).RunAsync(context, hashQueue.Reader, options.HashingThreads, failure.Token), failure);

            await Task.WhenAll(enumeration, pendingFiles, hashing).ConfigureAwait(false);
            await producers.ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            Finish(item, scanId, cancelled: false, failed: null, counters);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CompleteQuietlyAsync(writer).ConfigureAwait(false);
            Finish(item, scanId, cancelled: true, failed: null, counters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan of {MediaId} failed", item.MediaId);
            await CompleteQuietlyAsync(writer).ConfigureAwait(false);
            Finish(item, scanId, cancelled: false, failed: ex, counters);
        }
    }

    /// <summary>Runs a phase; if it fails (not by cancellation) the other phases are cancelled too.</summary>
    private static async Task RunGuardedAsync(Func<Task> phase, CancellationTokenSource failure)
    {
        try
        {
            await phase().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await failure.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Feeds files still pending from earlier runs to the hashing phase, in short read transactions.</summary>
    private async Task LoadPendingFilesAsync(long mediaKey, long maxFileId, ScanCounters counters, ChannelWriter<HashWork> queue, CancellationToken cancellationToken)
    {
        var after = 0L;
        while (true)
        {
            List<PendingFile> page;
            using (var scope = _session.Database.Open())
            {
                page = scope.Connection.Query<PendingFile>(
                    """
                    SELECT f.FileId, fo.RelativePath || f.Name AS RelativePath, f.SizeBytes, f.ModifiedUtc
                    FROM File f JOIN Folder fo ON fo.FolderId = f.FolderId
                    WHERE f.MediaKey = @mediaKey AND f.HashStatus = 0 AND f.FileId > @after AND f.FileId <= @maxFileId
                    ORDER BY f.FileId LIMIT @limit
                    """,
                    new { mediaKey, after, maxFileId, limit = PendingPageSize }).AsList();
            }

            if (page.Count == 0)
            {
                return;
            }

            counters.AddFiles(page.Count, page.Sum(f => f.SizeBytes));
            foreach (var file in page)
            {
                var modified = Core.Time.UtcTimestamp.TryParse(file.ModifiedUtc, out var m) ? m : (DateTimeOffset?)null;
                await queue.WriteAsync(new HashWork(file.FileId, file.RelativePath, file.SizeBytes, modified), cancellationToken).ConfigureAwait(false);
            }

            after = page[^1].FileId;
        }
    }

    private async Task OnNetworkUnavailableAsync(Exception exception, CancellationToken cancellationToken)
    {
        var message = $"The share or network is unavailable ({exception.Message}). The scan was paused; resume it when the connection is back.";
        PauseGate? pause;
        bool pauseNow;
        lock (_gate)
        {
            pause = _pause;
            pauseNow = pause is { IsPaused: false };
            if (pauseNow)
            {
                _notes.Add($"{_timeProvider.GetUtcNow():u} Paused automatically: network unavailable ({exception.Message}).");
            }
        }

        if (pauseNow)
        {
            PauseCore("network", exception.Message);
            AutoPaused?.Invoke(this, message);
        }

        if (pause is not null)
        {
            await pause.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void Finish(ScanQueueItem item, long scanId, bool cancelled, Exception? failed, ScanCounters counters)
    {
        lock (_statusGate)
        {
            _finishing = true;
            FinishCore(item, scanId, cancelled, failed, counters);
        }
    }

    private void FinishCore(ScanQueueItem item, long scanId, bool cancelled, Exception? failed, ScanCounters counters)
    {
        var now = _timeProvider.GetUtcNow();
        ScanOutcome outcome;
        MediaStatus status;
        ScanTotals totals;
        string? notes;
        lock (_gate)
        {
            notes = _notes.Count > 0 ? string.Join(Environment.NewLine, _notes) : null;
        }

        using (var scope = _session.Database.Open())
        using (var tx = scope.BeginTransaction())
        {
            var media = new MediaRepository(scope);
            var scanData = new ScanDataRepository(scope);
            totals = scanData.ComputeTotals(item.MediaKey);
            if (failed is null && !cancelled)
            {
                new SummaryRepository(scope).RebuildForMedia(item.MediaKey);
                outcome = totals.ErrorCount > 0 ? ScanOutcome.CompletedWithErrors : ScanOutcome.Completed;
                status = totals.ErrorCount > 0 ? MediaStatus.CompletedWithErrors : MediaStatus.Completed;
                media.MarkScanCompleted(item.MediaKey, now);
                _session.Audit.Write(scope, AuditAction.ScanCompleted, item.MediaId, new { ScanId = scanId, Outcome = outcome.ToString(), totals.FileCount, totals.TotalBytes, totals.ErrorCount });
            }
            else if (cancelled)
            {
                lock (_gate)
                {
                    outcome = _cancelOutcome;
                }

                status = MediaStatus.Incomplete;
                _session.Audit.Write(scope, AuditAction.ScanCancelled, item.MediaId, new { ScanId = scanId, Outcome = outcome.ToString() });
            }
            else
            {
                outcome = ScanOutcome.Failed;
                status = MediaStatus.Incomplete;
                notes = string.Join(Environment.NewLine, new[] { notes, failed!.Message }.Where(n => n is not null));
                _session.Audit.Write(scope, AuditAction.ScanFailed, item.MediaId, new { ScanId = scanId, Error = failed.Message });
            }

            media.UpdateTotals(item.MediaKey, totals);
            media.SetStatus(item.MediaKey, status);
            new ScanLogRepository(scope).Finish(scanId, now, outcome, totals, notes);
            tx.Commit();
        }

        RaiseStatus(item.MediaKey, status);
        PublishProgress(item, new ScanProgressTracker(_timeProvider), counters, null);
        ScanFinished?.Invoke(this, new ScanFinishedEventArgs(item, outcome, totals, failed?.Message));
    }

    private static async Task CompleteQuietlyAsync(ScanDbWriter writer)
    {
        try
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Writer failures are already reported through the scan failure.
        }
    }

    private void SetStatus(long mediaKey, MediaStatus status, AuditAction? audit = null, object? details = null)
    {
        try
        {
            using var scope = _session.Database.Open();
            using var tx = scope.BeginTransaction();
            new MediaRepository(scope).SetStatus(mediaKey, status);
            if (audit is { } action)
            {
                var mediaId = new MediaRepository(scope).Get(mediaKey)?.MediaId;
                _session.Audit.Write(scope, action, mediaId, details);
            }

            tx.Commit();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogWarning(ex, "Could not update the status of media {MediaKey} to {Status}", mediaKey, status);
        }

        RaiseStatus(mediaKey, status);
    }

    private void RaiseStatus(long mediaKey, MediaStatus status) =>
        MediaStatusChanged?.Invoke(this, new MediaStatusChangedEventArgs(mediaKey, status));

    private void PublishProgress(ScanQueueItem item, ScanProgressTracker tracker, ScanCounters counters, PauseGate? pause)
    {
        var phase = counters.EnumerationDone || item.Type == ScanType.RetryFailed ? ScanPhase.Hashing : ScanPhase.Enumerating;
        ProgressChanged?.Invoke(this, tracker.Sample(item.MediaKey, item.MediaId, phase, pause?.IsPaused == true, counters));
    }

    private sealed class PendingFile
    {
        public long FileId { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string? ModifiedUtc { get; set; }
    }
}

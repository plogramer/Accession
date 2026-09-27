using System.ComponentModel;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Accession.App.Services;

/// <summary>Owns the scan coordinator of the open inventory and exposes its state to the UI thread.</summary>
public sealed class ScanHost : ObservableObject
{
    private readonly InventoryHost _host;
    private readonly ISettingsService _settings;
    private readonly IAppInfo _appInfo;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDialogService _dialogs;
    private readonly IDirectoryLister _lister = new FileSystemDirectoryLister();
    private readonly IFileHasher _hasher = new Sha1FileHasher();
    private ScanCoordinator? _coordinator;

    public ScanHost(InventoryHost host, ISettingsService settings, IAppInfo appInfo, TimeProvider timeProvider,
        ILoggerFactory loggerFactory, IDialogService dialogs)
    {
        _host = host;
        _settings = settings;
        _appInfo = appInfo;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _dialogs = dialogs;
        _host.SessionChanged += (_, _) => SyncCoordinator();
        _host.PropertyChanged += OnHostChanged;
    }

    public ScanCoordinator? Coordinator => _coordinator;

    /// <summary>Scanning is possible: writable session with a reachable root.</summary>
    public bool CanScan => _coordinator is not null && _host.CanModify && _host.IsRootAvailable;

    public bool IsBusy => _coordinator?.IsBusy ?? false;

    public CoordinatorState State => _coordinator?.State ?? CoordinatorState.Idle;

    public bool CanPause => State == CoordinatorState.Running;

    public bool CanResume => State == CoordinatorState.Paused;

    public bool CanCancel => State != CoordinatorState.Idle;

    public ScanProgressSnapshot? Progress { get; private set; }

    public ScanQueueItem? Current => _coordinator?.Current;

    public IReadOnlyList<ScanQueueItem> Waiting => _coordinator?.Waiting ?? [];

    /// <summary>Running item plus waiting items.</summary>
    public int QueueLength => (Current is null ? 0 : 1) + Waiting.Count;

    public ScanOptions? CurrentOptions => Current is null ? null : _coordinator?.CurrentOptions;

    /// <summary>One line for the status bar, e.g. "Scanning 123-123_002 – Hashing 48 % – 182 MB/s".</summary>
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>Queues scans; shows the reasons for media that could not be queued.</summary>
    public void Enqueue(IEnumerable<(Media Media, ScanType Type)> requests)
    {
        if (_coordinator is not { } coordinator)
        {
            _dialogs.ShowWarning("Scan", "Scanning is not available: the inventory is read-only or the root folder cannot be reached.");
            return;
        }

        var problems = new List<string>();
        foreach (var (media, type) in requests)
        {
            try
            {
                coordinator.Enqueue(media.MediaKey, type);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
            {
                problems.Add($"• {media.MediaId}: {ex.Message}");
            }
        }

        if (problems.Count > 0)
        {
            _dialogs.ShowWarning("Scan", "Some media could not be queued:\n\n" + string.Join(Environment.NewLine, problems));
        }

        Refresh();
    }

    public void MoveUp(long mediaKey) => Run(c => c.MoveUp(mediaKey));

    public void MoveDown(long mediaKey) => Run(c => c.MoveDown(mediaKey));

    public void Remove(long mediaKey) => Run(c => c.Remove(mediaKey));

    public void Pause() => Run(c => c.Pause());

    public void Resume() => Run(c => c.Resume());

    public void Cancel()
    {
        if (_coordinator?.Current is { } current &&
            _dialogs.Confirm("Cancel scan", $"Cancel the scan of '{current.MediaId}'? It stays incomplete and can be resumed later."))
        {
            Run(c => c.Cancel());
        }
    }

    /// <summary>Interrupts any running scan (resumable later) and disposes the coordinator.</summary>
    public async Task ShutdownAsync()
    {
        var coordinator = _coordinator;
        if (coordinator is null)
        {
            return;
        }

        _coordinator = null;
        Detach(coordinator);
        await coordinator.DisposeAsync().ConfigureAwait(false);
        UiThread.Post(() =>
        {
            Progress = null;
            Refresh();
        });
    }

    private void SyncCoordinator()
    {
        var shouldHave = _host.CanModify && _host.IsRootAvailable;
        if (shouldHave && _coordinator is null && _host.Session is { } session)
        {
            var coordinator = new ScanCoordinator(session, _lister, _hasher, BuildOptions, _appInfo, _timeProvider,
                _loggerFactory.CreateLogger<ScanCoordinator>());
            coordinator.StateChanged += OnStateChanged;
            coordinator.ProgressChanged += OnProgress;
            coordinator.MediaStatusChanged += OnMediaStatusChanged;
            coordinator.ScanFinished += OnScanFinished;
            coordinator.AutoPaused += OnAutoPaused;
            _coordinator = coordinator;
        }
        else if (!shouldHave && _coordinator is not null)
        {
            _ = ShutdownAsync();
        }

        Refresh();
    }

    private ScanOptions BuildOptions()
    {
        var s = _settings.Current;
        return new ScanOptions(s.EnumerationThreads, s.HashingThreads, s.DbBatchSize);
    }

    private void Detach(ScanCoordinator coordinator)
    {
        coordinator.StateChanged -= OnStateChanged;
        coordinator.ProgressChanged -= OnProgress;
        coordinator.MediaStatusChanged -= OnMediaStatusChanged;
        coordinator.ScanFinished -= OnScanFinished;
        coordinator.AutoPaused -= OnAutoPaused;
    }

    private void Run(Action<ScanCoordinator> action)
    {
        if (_coordinator is { } coordinator)
        {
            // Status updates touch the database; keep the UI thread free.
            Task.Run(() => action(coordinator)).ContinueWith(_ => UiThread.Post(Refresh), TaskScheduler.Default);
        }
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_host.HasSession)
        {
            SyncCoordinator();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => UiThread.Post(Refresh);

    private void OnProgress(object? sender, ScanProgressSnapshot snapshot) => UiThread.Post(() =>
    {
        Progress = snapshot;
        Refresh();
    });

    private void OnMediaStatusChanged(object? sender, MediaStatusChangedEventArgs e) => _host.NotifyMediaChanged();

    private void OnScanFinished(object? sender, ScanFinishedEventArgs e)
    {
        _host.NotifyMediaChanged();
        var text = e.Outcome switch
        {
            ScanOutcome.Completed => $"Scan of {e.Item.MediaId} completed: {e.Totals.FileCount:N0} files.",
            ScanOutcome.CompletedWithErrors => $"Scan of {e.Item.MediaId} completed with {e.Totals.ErrorCount:N0} error(s): {e.Totals.FileCount:N0} files. See Errors.",
            ScanOutcome.Cancelled => $"Scan of {e.Item.MediaId} was cancelled. It can be resumed.",
            ScanOutcome.Interrupted => $"Scan of {e.Item.MediaId} was interrupted. It can be resumed.",
            _ => $"Scan of {e.Item.MediaId} failed: {e.Message}",
        };
        UiThread.Post(() => _host.SetNotice(text));
    }

    private void OnAutoPaused(object? sender, string message) => UiThread.Post(() => _dialogs.ShowWarning("Scan paused", message));

    private void Refresh()
    {
        StatusText = BuildStatusText();
        OnPropertyChanged(string.Empty);
    }

    private string BuildStatusText()
    {
        if (_coordinator?.Current is not { } current)
        {
            return string.Empty;
        }

        var waiting = _coordinator.Waiting.Count;
        var queued = waiting > 0 ? $" (+{waiting} queued)" : string.Empty;
        if (State == CoordinatorState.Paused)
        {
            return $"Scan paused: {current.MediaId}{queued}";
        }

        if (Progress is not { } p || p.MediaKey != current.MediaKey)
        {
            return $"Scanning {current.MediaId}…{queued}";
        }

        var unit = _settings.Current.SizeUnit;
        var rate = p.BytesPerSecond > 0 ? $" – {SizeFormatter.Format((long)p.BytesPerSecond, unit)}/s" : string.Empty;
        return p.Phase == ScanPhase.Enumerating
            ? $"Scanning {current.MediaId} – listing: {p.FilesFound:N0} files, {p.FoldersFound:N0} folders{queued}"
            : $"Scanning {current.MediaId} – hashing {p.PercentByBytes:0} %{rate}{queued}";
    }
}

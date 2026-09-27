using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using Accession.Data.Scanning;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.UI.MediaScreen;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.ViewModels.MediaScreen;

/// <summary>Media screen (requirements 8.6): registered media, details and scan history.</summary>
public sealed partial class MediaListViewModel : ViewModelBase, IMediaModel, IDisposable
{
    private readonly IUiDispatcher _ui;
    private readonly InventoryHost _host;
    private readonly MediaWorkflows _workflows;
    private readonly ISettingsService _settings;
    private readonly ILogger<MediaListViewModel> _logger;
    private readonly ScanHost _scans;
    private readonly FileBrowserNavigator _navigator;
    private IReadOnlyList<MediaRowViewModel> _selectedRows = [];

    public MediaListViewModel(InventoryHost host, MediaWorkflows workflows, ScanHost scans, ISettingsService settings, ILogger<MediaListViewModel> logger, IUiDispatcher ui,
        FileBrowserNavigator navigator)
    {
        _navigator = navigator;
        _ui = ui;
        _host = host;
        _scans = scans;
        _scans.PropertyChanged += OnHostChanged;
        _workflows = workflows;
        _settings = settings;
        _logger = logger;
        _host.MediaChanged += OnMediaChanged;
        _host.PropertyChanged += OnHostChanged;
        _settings.SettingsChanged += OnSettingsChanged;
        Load();
    }

    public ObservableCollection<MediaRowViewModel> Rows { get; } = [];

    public ObservableCollection<ScanHistoryRow> ScanHistory { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(OpenInExplorerCommand), nameof(OpenFilesCommand))]
    public partial MediaRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    public partial string DetailsPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailsAdded { get; set; } = string.Empty;

    public bool HasSelection => SelectedRow is not null;

    public bool HasRows => Rows.Count > 0;

    public bool IsReadOnly => _host.IsReadOnly;

    public long? ScanningMediaKey => _scans.Current?.MediaKey;

    public string LiveProgressText
    {
        get
        {
            if (_scans.Current is not { } current)
            {
                return string.Empty;
            }

            if (_scans.State == CoordinatorState.Paused)
            {
                return "Paused";
            }

            if (_scans.Progress is not { } p || p.MediaKey != current.MediaKey)
            {
                return "Starting…";
            }

            var culture = CultureInfo.CurrentCulture;
            if (p.Phase == ScanPhase.Enumerating)
            {
                return $"Listing: {p.FilesFound.ToString("N0", culture)} files in {p.FoldersFound.ToString("N0", culture)} folders";
            }

            var unit = _settings.Current.SizeUnit;
            var parts = new List<string> { $"Hashing {p.FilesHashed.ToString("N0", culture)} of {p.FilesFound.ToString("N0", culture)} files" };
            if (p.BytesPerSecond > 0)
            {
                parts.Add(SizeFormatter.Format((long)p.BytesPerSecond, unit) + "/s");
            }

            if (p.Eta is { } eta && eta > TimeSpan.Zero)
            {
                parts.Add(eta.TotalMinutes < 1 ? "under a minute left" : $"{Math.Ceiling(eta.TotalMinutes).ToString("N0", culture)} min left");
            }

            return string.Join(" · ", parts);
        }
    }

    public double? LiveProgressRatio =>
        _scans.Current is { } current && _scans.Progress is { Phase: ScanPhase.Hashing or ScanPhase.Finalizing } p && p.MediaKey == current.MediaKey
            ? p.PercentByBytes / 100
            : null;

    ICommand IMediaModel.AddMediaCommand => AddMediaCommand;

    ICommand IMediaModel.DiscoverCommand => DiscoverCommand;

    ICommand IMediaModel.DeleteCommand => DeleteCommand;

    ICommand IMediaModel.ScanCommand => ScanCommand;

    ICommand IMediaModel.RescanCommand => RescanCommand;

    ICommand IMediaModel.ResumeCommand => ResumeCommand;

    ICommand IMediaModel.RetryFailedCommand => RetryFailedCommand;

    ICommand IMediaModel.OpenInExplorerCommand => OpenInExplorerCommand;

    ICommand IMediaModel.OpenFilesCommand => OpenFilesCommand;

    ICommand IMediaModel.RefreshCommand => RefreshCommand;

    /// <summary>Raised after the list was reloaded (the shell updates its badge).</summary>
    public event EventHandler? RowsReloaded;

    /// <summary>Called by the view when the grid's (multi-)selection changes.</summary>
    public void SetSelectedRows(IEnumerable<MediaRowViewModel> rows)
    {
        _selectedRows = rows.ToList();
        NotifyScanCommands();
    }

    public void Dispose()
    {
        _scans.PropertyChanged -= OnHostChanged;
        _host.MediaChanged -= OnMediaChanged;
        _host.PropertyChanged -= OnHostChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
    }

    [RelayCommand(CanExecute = nameof(CanAddMedia))]
    private Task AddMedia() => _workflows.AddMediaAsync();

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private Task Discover() => _workflows.RunDiscoveryAsync(DiscoveryMode.Manual);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task Delete() => SelectedRow is { } row ? _workflows.DeleteMediaAsync(row.Media) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanScanSelected))]
    private void Scan() => _workflows.Scan(Selected(m => m.Status is MediaStatus.New or MediaStatus.Incomplete or MediaStatus.Completed or MediaStatus.CompletedWithErrors), null);

    [RelayCommand(CanExecute = nameof(CanRescanSelected))]
    private void Rescan() => _workflows.Scan(Selected(IsRescannable), ScanType.Full);

    [RelayCommand(CanExecute = nameof(CanResumeSelected))]
    private void Resume() => _workflows.Scan(Selected(m => m.Status == MediaStatus.Incomplete), ScanType.Resume);

    [RelayCommand(CanExecute = nameof(CanRetrySelected))]
    private void RetryFailed() => _workflows.Scan(Selected(IsRetryable), ScanType.RetryFailed);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInExplorer()
    {
        if (SelectedRow is { } row)
        {
            _workflows.OpenInExplorer(row.Media);
        }
    }

    /// <summary>Opens the Files screen for the selected media.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFiles()
    {
        if (SelectedRow is { } row)
        {
            _navigator.ShowFiles(new FileFilter { MediaKey = row.Media.MediaKey });
        }
    }

    [RelayCommand]
    private void Refresh() => Load();

    private static bool IsRescannable(Media m) => m.Status is MediaStatus.Completed or MediaStatus.CompletedWithErrors or MediaStatus.Incomplete;

    private static bool IsRetryable(Media m) => m.Status is MediaStatus.CompletedWithErrors or MediaStatus.Incomplete;

    private List<Media> Selected(Func<Media, bool> predicate) =>
        (_selectedRows.Count > 0 ? _selectedRows : SelectedRow is { } row ? [row] : [])
            .Select(r => r.Media).Where(predicate).ToList();

    private bool CanScanSelected() => _scans.CanScan && Selected(m => m.Status is MediaStatus.New or MediaStatus.Incomplete or MediaStatus.Completed or MediaStatus.CompletedWithErrors).Count > 0;

    private bool CanRescanSelected() => _scans.CanScan && Selected(IsRescannable).Count > 0;

    private bool CanResumeSelected() => _scans.CanScan && Selected(m => m.Status == MediaStatus.Incomplete).Count > 0;

    private bool CanRetrySelected() => _scans.CanScan && Selected(IsRetryable).Count > 0;

    private void NotifyScanCommands()
    {
        ScanCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddMedia() => _workflows.CanAddMedia;

    private bool CanDiscover() => _workflows.CanDiscover;

    private bool CanDelete() => SelectedRow is { } row && _host.CanModify && !MediaService.BusyStatuses.Contains(row.Media.Status);

    partial void OnSelectedRowChanged(MediaRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        NotifyScanCommands();
        LoadDetails();
    }

    private void Load()
    {
        var selectedKey = SelectedRow?.Media.MediaKey;
        _selectedRows = [];
        Rows.Clear();
        if (_host.Session is { } session)
        {
            try
            {
                var settings = _settings.Current;
                using var scope = session.Database.Open();
                foreach (var media in new MediaRepository(scope).ListActive())
                {
                    Rows.Add(new MediaRowViewModel(media, settings.SizeUnit, settings.DisplayTimeZone));
                }
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
            {
                _logger.LogError(ex, "Loading media failed");
            }
        }

        SelectedRow = Rows.FirstOrDefault(r => r.Media.MediaKey == selectedKey);
        OnPropertyChanged(nameof(HasRows));
        RowsReloaded?.Invoke(this, EventArgs.Empty);
    }

    private void LoadDetails()
    {
        ScanHistory.Clear();
        if (SelectedRow is not { } row || _host.Session is not { } session)
        {
            DetailsPath = string.Empty;
            DetailsAdded = string.Empty;
            return;
        }

        var zone = _settings.Current.DisplayTimeZone;
        DetailsPath = MediaFolders.FullPath(session.Config.RootPath, row.MediaId);
        DetailsAdded = $"Added {TimeFormatter.Format(row.Media.AddedAtUtc, zone)} by {row.Media.AddedBy}";
        try
        {
            using var scope = session.Database.Open();
            foreach (var scan in new ScanLogRepository(scope).ListByMedia(row.Media.MediaKey))
            {
                ScanHistory.Add(new ScanHistoryRow(
                    scan.ScanId,
                    scan.ScanType.ToString(),
                    TimeFormatter.Format(scan.StartedAtUtc, zone),
                    scan.EndedAtUtc is { } ended ? TimeFormatter.Format(ended, zone) : "running",
                    scan.Outcome?.ToString() ?? string.Empty,
                    $"{scan.UserName} on {scan.MachineName}",
                    scan.FileCount?.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty));
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading scan history failed");
        }
    }

    private void OnMediaChanged(object? sender, EventArgs e) => Load();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => _ui.Post(Load);

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ScanningMediaKey));
        OnPropertyChanged(nameof(LiveProgressText));
        OnPropertyChanged(nameof(LiveProgressRatio));
        OnPropertyChanged(nameof(IsReadOnly));
        AddMediaCommand.NotifyCanExecuteChanged();
        DiscoverCommand.NotifyCanExecuteChanged();
        NotifyScanCommands();
    }
}

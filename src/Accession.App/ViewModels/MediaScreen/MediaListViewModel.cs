using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.App.ViewModels.MediaScreen;

/// <summary>Media screen (requirements 8.6): registered media, details and scan history.</summary>
public sealed partial class MediaListViewModel : ViewModelBase, IDisposable
{
    private readonly InventoryHost _host;
    private readonly MediaWorkflows _workflows;
    private readonly ISettingsService _settings;
    private readonly ILogger<MediaListViewModel> _logger;

    public MediaListViewModel(InventoryHost host, MediaWorkflows workflows, ISettingsService settings, ILogger<MediaListViewModel> logger)
    {
        _host = host;
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
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(OpenInExplorerCommand))]
    public partial MediaRowViewModel? SelectedRow { get; set; }

    [ObservableProperty]
    public partial string DetailsPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailsAdded { get; set; } = string.Empty;

    public bool HasSelection => SelectedRow is not null;

    public bool HasRows => Rows.Count > 0;

    /// <summary>Raised after the list was reloaded (the shell updates its badge).</summary>
    public event EventHandler? RowsReloaded;

    public void Dispose()
    {
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInExplorer()
    {
        if (SelectedRow is { } row)
        {
            _workflows.OpenInExplorer(row.Media);
        }
    }

    [RelayCommand]
    private void Refresh() => Load();

    private bool CanAddMedia() => _workflows.CanAddMedia;

    private bool CanDiscover() => _workflows.CanDiscover;

    private bool CanDelete() => SelectedRow is { } row && _host.CanModify && !MediaService.BusyStatuses.Contains(row.Media.Status);

    partial void OnSelectedRowChanged(MediaRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        LoadDetails();
    }

    private void Load()
    {
        var selectedKey = SelectedRow?.Media.MediaKey;
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

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => UiThread.Post(Load);

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        AddMediaCommand.NotifyCanExecuteChanged();
        DiscoverCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}

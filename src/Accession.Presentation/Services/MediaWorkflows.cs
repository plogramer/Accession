using System.IO;
using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Data.MediaManagement;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

public enum DiscoveryMode
{
    /// <summary>After opening or creating: prompt for new folders, update the notice.</summary>
    OnOpen,

    /// <summary>Inventory → Discover Media: always report the outcome.</summary>
    Manual,

    /// <summary>Refresh statuses and the notice without prompting.</summary>
    Silent,
}

/// <summary>User-level media actions: discover, add, delete, open in Explorer.</summary>
public sealed class MediaWorkflows
{
    private readonly IDesktop _desktop;
    private readonly InventoryHost _host;
    private readonly IDialogService _dialogs;
    private readonly BusyTracker _busy;
    private readonly MediaDiscoveryService _discovery;
    private readonly MediaService _media;
    private readonly ISettingsService _settings;
    private readonly ILogger<MediaWorkflows> _logger;
    private readonly ScanHost _scans;

    public MediaWorkflows(
        InventoryHost host,
        IDialogService dialogs,
        BusyTracker busy,
        MediaDiscoveryService discovery,
        MediaService media,
        ISettingsService settings,
        ScanHost scans,
        ILogger<MediaWorkflows> logger,
        IDesktop desktop)
    {
        _desktop = desktop;
        _scans = scans;
        _host = host;
        _dialogs = dialogs;
        _busy = busy;
        _discovery = discovery;
        _media = media;
        _settings = settings;
        _logger = logger;
    }

    public bool CanDiscover => _host.HasSession && _host.IsRootAvailable;

    public bool CanAddMedia => _host.CanModify && _host.IsRootAvailable;

    /// <summary>Compares the root's subfolders with registered media and reacts according to <paramref name="mode"/>.</summary>
    public async Task RunDiscoveryAsync(DiscoveryMode mode)
    {
        if (_host.Session is not { } session || !session.IsRootAvailable)
        {
            if (mode == DiscoveryMode.Manual)
            {
                _dialogs.ShowWarning("Discover media", "The root folder is not available, so media folders cannot be checked.");
            }

            return;
        }

        DiscoveryResult result;
        try
        {
            result = await _busy.RunAsync(_ => Task.FromResult(_discovery.Discover(session)), "Checking media folders…");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Media discovery failed");
            if (mode == DiscoveryMode.Manual)
            {
                _dialogs.ShowError("Discover media", $"Media folders could not be checked.\n\n{ex.Message}", ex);
            }

            return;
        }

        _host.NotifyMediaChanged();
        if (!result.RootAvailable)
        {
            _host.SetNotice("The root folder cannot be reached.");
            if (mode == DiscoveryMode.Manual)
            {
                _dialogs.ShowWarning("Discover media", "The root folder cannot be reached.");
            }

            return;
        }

        UpdateNotice(result);
        if (mode == DiscoveryMode.Silent)
        {
            return;
        }

        if (result.NewFolders.Count > 0 && _host.CanModify)
        {
            ShowAddDialog("New Media Found", result.NewFolders);
        }
        else if (result.NewFolders.Count > 0 && mode == DiscoveryMode.Manual)
        {
            _dialogs.ShowInfo("Discover media",
                $"{result.NewFolders.Count:N0} folder(s) under the root are not in this inventory. Open the inventory with write access to add them.");
        }
        else if (mode == DiscoveryMode.Manual)
        {
            _dialogs.ShowInfo("Discover media", "No new media folders were found under the root folder.");
        }
    }

    /// <summary>Media → Add Media: shows unregistered folders under the root plus a folder browser.</summary>
    public async Task AddMediaAsync()
    {
        if (_host.Session is not { } session || !CanAddMedia)
        {
            return;
        }

        IReadOnlyList<DiscoveredFolder> folders = [];
        try
        {
            var result = await _busy.RunAsync(_ => Task.FromResult(_discovery.Discover(session)), "Checking media folders…");
            folders = result.NewFolders;
            UpdateNotice(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogWarning(ex, "Listing media folders failed");
        }

        ShowAddDialog("Add Media", folders);
    }

    public async Task DeleteMediaAsync(Media media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (_host.Session is not { } session || !_host.CanModify)
        {
            return;
        }

        if (MediaService.BusyStatuses.Contains(media.Status))
        {
            _dialogs.ShowWarning("Delete media", $"'{media.MediaId}' is being scanned. Cancel the scan before deleting it.");
            return;
        }

        var dialog = new DeleteMediaViewModel(media, _settings.Current.SizeUnit);
        if (_dialogs.ShowDialog(dialog) != true)
        {
            return;
        }

        try
        {
            await _busy.RunAsync(_ => Task.FromResult(_media.Delete(session, media.MediaKey)), $"Deleting {media.MediaId}…");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Deleting media {MediaId} failed", media.MediaId);
            _dialogs.ShowError("Delete media", $"'{media.MediaId}' could not be deleted.\n\n{ex.Message}", ex);
        }

        await RunDiscoveryAsync(DiscoveryMode.Silent);
    }

    /// <summary>Notice bar "Scan now": full scan for never-scanned media, resume for incomplete ones (DSC-04).</summary>
    public void ScanPendingMedia()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        IReadOnlyList<Media> media;
        using (var scope = session.Database.Open())
        {
            media = new Accession.Data.Repositories.MediaRepository(scope).ListActive();
        }

        var requests = media
            .Where(m => m.Status is MediaStatus.New or MediaStatus.Incomplete)
            .Select(m => (m, m.Status == MediaStatus.New ? ScanType.Full : ScanType.Resume))
            .ToList();
        if (requests.Count > 0)
        {
            _ = _scans.EnqueueAsync(requests);
            _host.SetNotice(_host.Session?.OpenNotice ?? string.Empty);
        }
    }

    /// <summary>
    /// Media screen scan actions. <paramref name="type"/> null = "Scan": New → full scan, Incomplete → resume,
    /// already scanned → rescan after confirmation.
    /// </summary>
    public void Scan(IReadOnlyCollection<Media> media, ScanType? type)
    {
        if (media.Count == 0)
        {
            return;
        }

        var requests = media.Select(m => (m, type ?? (m.Status switch
        {
            MediaStatus.Incomplete => ScanType.Resume,
            _ => ScanType.Full,
        }))).ToList();

        var rescans = requests.Where(r => r.Item2 == ScanType.Full && r.m.Status is MediaStatus.Completed or MediaStatus.CompletedWithErrors).ToList();
        if (rescans.Count > 0 && !_dialogs.Confirm("Rescan",
                $"Rescanning replaces the existing results of {rescans.Count:N0} media " +
                $"({string.Join(", ", rescans.Take(5).Select(r => r.m.MediaId))}{(rescans.Count > 5 ? ", …" : string.Empty)}). Continue?"))
        {
            return;
        }

        _ = _scans.EnqueueAsync(requests);
    }

    public void OpenInExplorer(Media media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (_host.Config is not { } config)
        {
            return;
        }

        var path = MediaFolders.FullPath(config.RootPath, media.MediaId);
        if (!Directory.Exists(path))
        {
            _dialogs.ShowWarning("Open in Explorer", $"The folder '{path}' cannot be found.");
            return;
        }

        _desktop.OpenFolder(path);
    }

    private void ShowAddDialog(string title, IReadOnlyList<DiscoveredFolder> folders)
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        var dialog = new AddMediaViewModel(title, session, folders, _media, _dialogs, _scans.CanScan);
        if (_dialogs.ShowDialog(dialog) == true)
        {
            _host.NotifyMediaChanged();
            if (dialog.StartScanning && dialog.Added.Count > 0)
            {
                _ = _scans.EnqueueAsync([.. dialog.Added.Select(m => (m, ScanType.Full))]);
            }

            _ = RunDiscoveryAsync(DiscoveryMode.Silent);
        }
    }

    private void UpdateNotice(DiscoveryResult result)
    {
        var parts = new List<string>();
        if (_host.Session?.OpenNotice is { Length: > 0 } openNotice)
        {
            parts.Add(openNotice); // e.g. "the last session was not closed properly" stays visible
        }

        if (result.NeverScanned.Count > 0)
        {
            parts.Add($"{result.NeverScanned.Count:N0} media not scanned yet");
        }

        if (result.Incomplete.Count > 0)
        {
            parts.Add($"{result.Incomplete.Count:N0} media with an incomplete scan");
        }

        if (result.MissingMedia.Count > 0)
        {
            parts.Add($"{result.MissingMedia.Count:N0} media folder(s) missing from the root");
        }

        _host.SetNotice(string.Join(" · ", parts));
    }
}

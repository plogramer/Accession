using System.Diagnostics;
using System.IO;
using Accession.Core.Inventories;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Data.Migrations;
using Accession.Data.Sessions;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

/// <summary>User-level inventory actions: new, open, close, properties, change root, settings.</summary>
public sealed class InventoryWorkflows
{
    private readonly IDesktop _desktop;

    public const string FileFilter = Accession.Core.Inventories.InventoryFileName.OpenFilter;

    private readonly InventoryHost _host;
    private readonly IDialogService _dialogs;
    private readonly BusyTracker _busy;
    private readonly ISettingsService _settings;
    private readonly InventoryCreationService _creation;
    private readonly InventoryOpenService _opener;
    private readonly IOpenInteraction _interaction;
    private readonly Func<NewInventoryViewModel> _newInventory;
    private readonly Func<SettingsViewModel> _settingsDialog;
    private readonly Func<InventoryPropertiesViewModel> _properties;
    private readonly Func<ChangeRootPathViewModel> _changeRoot;
    private readonly ILogger<InventoryWorkflows> _logger;
    private readonly MediaWorkflows _media;
    private readonly ScanHost _scans;

    public InventoryWorkflows(
        InventoryHost host,
        IDialogService dialogs,
        BusyTracker busy,
        ISettingsService settings,
        InventoryCreationService creation,
        InventoryOpenService opener,
        IOpenInteraction interaction,
        Func<NewInventoryViewModel> newInventory,
        Func<SettingsViewModel> settingsDialog,
        Func<InventoryPropertiesViewModel> properties,
        Func<ChangeRootPathViewModel> changeRoot,
        MediaWorkflows media,
        ScanHost scans,
        ILogger<InventoryWorkflows> logger,
        IDesktop desktop)
    {
        _desktop = desktop;
        _media = media;
        _scans = scans;
        _host = host;
        _dialogs = dialogs;
        _busy = busy;
        _settings = settings;
        _creation = creation;
        _opener = opener;
        _interaction = interaction;
        _newInventory = newInventory;
        _settingsDialog = settingsDialog;
        _properties = properties;
        _changeRoot = changeRoot;
        _logger = logger;
    }

    public async Task NewInventoryAsync()
    {
        var dialog = _newInventory();
        if (_dialogs.ShowDialog(dialog) != true)
        {
            return;
        }

        var request = dialog.BuildRequest();
        if (!await CloseInventoryAsync())
        {
            return;
        }

        try
        {
            using (var busy = _busy.Begin("Creating inventory…"))
            {
                var session = await Task.Run(() => _creation.Create(request));
                busy.Update("Loading the inventory…");
                await Task.Delay(30); // let the page show the new message first
                Activate(session);
            }

            await _media.RunDiscoveryAsync(dialog.ShowDiscoveredMedia ? DiscoveryMode.OnOpen : DiscoveryMode.Silent);
        }
        catch (InventoryValidationException ex)
        {
            _dialogs.ShowError("Cannot create inventory", string.Join(Environment.NewLine, ex.Errors.Values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Creating inventory at {Path} failed", request.SavePath);
            _dialogs.ShowError("Cannot create inventory", $"The inventory could not be created.\n\n{ex.Message}", ex);
        }
    }

    /// <summary>Opens <paramref name="path"/>, or asks for a file when null.</summary>
    public async Task OpenInventoryAsync(string? path = null)
    {
        path ??= _dialogs.PickOpenFile("Open inventory", FileFilter);
        if (path is null)
        {
            return;
        }

        // On a network share even "does the file exist" can take seconds: check it with the progress shown.
        bool exists;
        using (_busy.Begin("Opening inventory…"))
        {
            exists = await Task.Run(() => File.Exists(path));
        }

        if (!exists)
        {
            if (_dialogs.Confirm("Inventory not found", $"The file '{path}' was not found.\n\nRemove it from the recent list?"))
            {
                _settings.RemoveRecentInventory(path);
            }

            return;
        }

        if (_host.Session is { } current && string.Equals(current.DbPath, Path.GetFullPath(path), PathRules.Comparison))
        {
            return; // already open
        }

        if (!await CloseInventoryAsync())
        {
            return;
        }

        try
        {
            InventorySession? session;
            using (var busy = _busy.Begin("Opening inventory…"))
            {
                // On a background thread: the open may ask questions, and web dialogs need the UI thread free to answer.
                var watch = Stopwatch.StartNew();
                session = await Task.Run(() => _opener.Open(path, _interaction));
                var opened = watch.ElapsedMilliseconds;
                if (session is not null)
                {
                    // The screens are built on the UI thread (they read the database in the background).
                    busy.Update("Loading the inventory…");
                    await Task.Delay(30); // let the page show the new message first
                    watch.Restart();
                    Activate(session);
                    _logger.LogInformation("Opened {Path}: file checks, lock and recovery {Opened} ms, screens {Screens} ms",
                        path, opened, watch.ElapsedMilliseconds);
                }
            }

            if (session is not null)
            {
                await _media.RunDiscoveryAsync(DiscoveryMode.OnOpen);
            }
        }
        catch (Exception ex) when (ex is NotAnInventoryException or SchemaTooNewException or InventoryInUseException
                                       or Accession.Data.InventoryNeedsRecoveryException)
        {
            _dialogs.ShowError("Cannot open inventory", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _logger.LogError(ex, "Opening inventory {Path} failed", path);
            _dialogs.ShowError("Cannot open inventory", $"The inventory could not be opened.\n\n{ex.Message}", ex);
        }
    }

    /// <summary>
    /// Closes the open inventory (audit + release lock). If a scan is running the user is asked first; the scan
    /// is interrupted and can be resumed later. Returns false if the user chose to keep the inventory open.
    /// </summary>
    public async Task<bool> CloseInventoryAsync()
    {
        if (!_host.HasSession)
        {
            return true;
        }

        if (_scans.IsBusy)
        {
            if (!_dialogs.Confirm("Scan in progress",
                    "A scan is running. Closing the inventory interrupts it; the media stays incomplete and can be resumed later.\n\nClose anyway?"))
            {
                return false;
            }
        }

        await _busy.RunAsync(_ => _scans.ShutdownAsync(), "Stopping scan…");
        CloseInventory();
        return true;
    }

    /// <summary>Closes the open inventory without asking (scan must already be stopped). Safe when none is open.</summary>
    private void CloseInventory()
    {
        try
        {
            _host.Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // The session is gone either way; the lock will become stale and can be taken over.
            _logger.LogError(ex, "Closing the inventory failed");
            _dialogs.ShowWarning("Close inventory", $"The inventory was closed, but the lock could not be released.\n\n{ex.Message}");
        }
    }

    public void OpenSettings() => _dialogs.ShowDialog(_settingsDialog());

    public async Task ShowPropertiesAsync()
    {
        if (!_host.HasSession)
        {
            return;
        }

        var rootBefore = _host.Config?.RootPath;
        _dialogs.ShowDialog(_properties());
        if (!string.Equals(rootBefore, _host.Config?.RootPath, StringComparison.Ordinal))
        {
            await _media.RunDiscoveryAsync(DiscoveryMode.OnOpen);
        }
    }

    public async Task ChangeRootPathAsync()
    {
        if (_host.CanModify && _dialogs.ShowDialog(_changeRoot()) == true)
        {
            await _media.RunDiscoveryAsync(DiscoveryMode.OnOpen);
        }
    }

    public void OpenMatterLink()
    {
        var url = _host.Config?.MatterUrl;
        if (url is not null && InventoryValidation.IsWebUrl(url))
        {
            _desktop.OpenUrl(url);
        }
    }

    private void Activate(InventorySession session)
    {
        _host.Open(session);
        _host.SetNotice(session.OpenNotice);
        _settings.AddRecentInventory(session.DbPath, $"{session.Config.ClientName} – {session.Config.MatterName}");
    }
}

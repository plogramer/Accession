using System.ComponentModel;
using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>The main window: its title, the close flow, and the app-wide commands the web UI's menus use.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDesktop _desktop;
    private readonly InventoryHost _host;
    private readonly InventoryWorkflows _workflows;
    private readonly MediaWorkflows _media;
    private readonly ExportWorkflow _export;

    public MainWindowViewModel(
        InventoryHost host,
        InventoryWorkflows workflows,
        MediaWorkflows media,
        ScanHost scans,
        IDialogService dialogs,
        BusyTracker busy,
        IDesktop desktop,
        ExportWorkflow export)
    {
        _export = export;
        _desktop = desktop;
        _host = host;
        _workflows = workflows;
        _media = media;
        Scans = scans;
        Scans.PropertyChanged += (_, _) =>
        {
            PauseScanCommand.NotifyCanExecuteChanged();
            ResumeScanCommand.NotifyCanExecuteChanged();
            CancelScanCommand.NotifyCanExecuteChanged();
        };
        Busy = busy;
        _host.PropertyChanged += OnHostChanged;
        _host.LockLost += (_, _) => dialogs.ShowWarning(
            "Inventory lock lost",
            "Another user took over this inventory's lock. It is now open read-only; your changes up to now were saved.");
    }

    public string Title
    {
        get
        {
            if (_host.Config is not { } config)
            {
                return "Accession";
            }

            var title = $"Accession – {config.ClientName} / {config.MatterName} ({config.MatterCode})";
            return _host.IsReadOnly ? title + " – READ-ONLY" : title;
        }
    }

    public BusyTracker Busy { get; }

    public ScanHost Scans { get; }

    public bool HasSession => _host.HasSession;

    public bool HasMatterUrl => !string.IsNullOrWhiteSpace(_host.Config?.MatterUrl);

    /// <summary>
    /// Called when the main window is asked to close. Closes the inventory (asking first if a scan is running).
    /// Returns false to keep the window open.
    /// </summary>
    public Task<bool> PrepareCloseAsync() => _workflows.CloseInventoryAsync();

    [RelayCommand]
    private Task NewInventory() => _workflows.NewInventoryAsync();

    [RelayCommand]
    private Task OpenInventory() => _workflows.OpenInventoryAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task CloseInventory() => _workflows.CloseInventoryAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task Export() => _export.ExportAsync();

    [RelayCommand(CanExecute = nameof(CanPauseScan))]
    private void PauseScan() => Scans.Pause();

    [RelayCommand(CanExecute = nameof(CanResumeScan))]
    private void ResumeScan() => Scans.Resume();

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => Scans.Cancel();

    [RelayCommand]
    private void OpenSettings() => _workflows.OpenSettings();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task ShowProperties() => _workflows.ShowPropertiesAsync();

    [RelayCommand(CanExecute = nameof(CanModify))]
    private Task ChangeRootPath() => _workflows.ChangeRootPathAsync();

    [RelayCommand(CanExecute = nameof(HasMatterUrl))]
    private void OpenMatterLink() => _workflows.OpenMatterLink();

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private Task DiscoverMedia() => _media.RunDiscoveryAsync(DiscoveryMode.Manual);

    [RelayCommand(CanExecute = nameof(CanAddMedia))]
    private Task AddMedia() => _media.AddMediaAsync();

    [RelayCommand]
    private void CancelBusy() => Busy.Cancel();

    [RelayCommand]
    private void Exit() => _desktop.RequestExit();

    private bool CanModify() => _host.CanModify;

    private bool CanDiscover() => _media.CanDiscover;

    private bool CanPauseScan() => Scans.CanPause;

    private bool CanResumeScan() => Scans.CanResume;

    private bool CanCancelScan() => Scans.CanCancel;

    private bool CanAddMedia() => _media.CanAddMedia;

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(HasMatterUrl));
        CloseInventoryCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        ChangeRootPathCommand.NotifyCanExecuteChanged();
        OpenMatterLinkCommand.NotifyCanExecuteChanged();
        DiscoverMediaCommand.NotifyCanExecuteChanged();
        AddMediaCommand.NotifyCanExecuteChanged();
    }
}

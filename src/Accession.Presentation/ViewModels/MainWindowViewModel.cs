using System.ComponentModel;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Shell;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDesktop _desktop;
    private readonly IUiDispatcher _ui;
    private readonly INavigationService _navigation;
    private readonly InventoryHost _host;
    private readonly InventoryWorkflows _workflows;
    private readonly IDialogService _dialogs;
    private readonly MediaWorkflows _media;
    private readonly ISettingsService _settings;
    private readonly FileBrowserNavigator _navigator;

    public MainWindowViewModel(
        INavigationService navigation,
        InventoryHost host,
        InventoryWorkflows workflows,
        MediaWorkflows media,
        ScanHost scans,
        IDialogService dialogs,
        BusyTracker busy,
        ISettingsService settings,
        FileBrowserNavigator navigator,
        IUiDispatcher ui,
        IDesktop desktop)
    {
        _desktop = desktop;
        _ui = ui;
        _settings = settings;
        _navigator = navigator;
        _navigation = navigation;
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
        _dialogs = dialogs;
        Busy = busy;
        _navigation.CurrentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentScreen));
            OnPropertyChanged(nameof(IsClassicMenuVisible));
        };
        _host.PropertyChanged += OnHostChanged;
        _host.SessionChanged += (_, _) => ShowScreenForSession();
        _host.LockLost += (_, _) => _dialogs.ShowWarning(
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

    public ViewModelBase? CurrentScreen => _navigation.Current;

    public BusyTracker Busy { get; }

    public ScanHost Scans { get; }

    public bool HasSession => _host.HasSession;

    /// <summary>The web shell draws its own top bar and menus.</summary>
    public bool IsClassicMenuVisible => CurrentScreen is not WebAppViewModel;

    /// <summary>Shows the web UI (preview) while an inventory is open.</summary>
    public bool UseWebUi
    {
        get => _settings.Current.UseWebUi;
        set
        {
            if (value == UseWebUi)
            {
                return;
            }

            _settings.Update(s => s.UseWebUi = value);
            OnPropertyChanged();
            ShowScreenForSession();
        }
    }

    public bool HasMatterUrl => !string.IsNullOrWhiteSpace(_host.Config?.MatterUrl);

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    public void Initialize() => ShowScreenForSession();

    /// <summary>
    /// Called when the main window is asked to close. Closes the inventory (asking first if a scan is running).
    /// Returns false to keep the window open.
    /// </summary>
    public Task<bool> PrepareCloseAsync() => _workflows.CloseInventoryAsync();

    /// <summary>The web UI could not start: switch to the classic screens and explain why.</summary>
    public void WebUiFailed(string message)
    {
        _ui.Defer(() =>
        {
            UseWebUi = false;
            _dialogs.ShowWarning("New UI", message); // UseWebUi is off now, so this is a classic message box
        });
    }

    /// <summary>
    /// Leaves the web UI for the classic screens, optionally opening a screen (navigation title) or the Files screen
    /// with a filter. Deferred, because it is called from inside a web view event and replaces the web view.
    /// </summary>
    public void SwitchToClassic(string? screen, FileFilter? filter)
    {
        _ui.Defer(() =>
        {
            UseWebUi = false;
            if (_navigation.Current is not InventoryShellViewModel shell)
            {
                return;
            }

            if (filter is not null)
            {
                _navigator.ShowFiles(filter);
            }
            else if (screen is not null)
            {
                shell.Select(screen);
            }
        });
    }

    [RelayCommand]
    private Task NewInventory() => _workflows.NewInventoryAsync();

    [RelayCommand]
    private Task OpenInventory() => _workflows.OpenInventoryAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task CloseInventory() => _workflows.CloseInventoryAsync();

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

    /// <summary>Debug-only menu item to verify global error handling.</summary>
    [RelayCommand]
    private static void ThrowTestException() =>
        throw new InvalidOperationException("Test exception from the Help menu (debug builds only).");

    private bool CanModify() => _host.CanModify;

    private bool CanDiscover() => _media.CanDiscover;

    private bool CanPauseScan() => Scans.CanPause;

    private bool CanResumeScan() => Scans.CanResume;

    /// <summary>Leaves the web UI, e.g. from the Start page.</summary>
    [RelayCommand]
    private void LeaveWebUi() => SwitchToClassic(null, null);

    private bool CanCancelScan() => Scans.CanCancel;

    private bool CanAddMedia() => _media.CanAddMedia;

    private void ShowScreenForSession()
    {
        if (UseWebUi && _desktop.WebViewRuntimeVersion() is null)
        {
            _settings.Update(s => s.UseWebUi = false);
            OnPropertyChanged(nameof(UseWebUi));
            _dialogs.ShowWarning("New UI",
                "The new UI needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer. " +
                "Accession is using the classic screens.\n\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and try again.");
        }

        if (UseWebUi)
        {
            // One web page for Start and the inventory shell; it follows session changes itself.
            if (_navigation.Current is not WebAppViewModel)
            {
                _navigation.NavigateTo<WebAppViewModel>();
            }
        }
        else if (_host.HasSession)
        {
            _navigation.NavigateTo<InventoryShellViewModel>();
        }
        else
        {
            _navigation.NavigateTo<StartViewModel>();
        }
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(HasMatterUrl));
        CloseInventoryCommand.NotifyCanExecuteChanged();
        ShowPropertiesCommand.NotifyCanExecuteChanged();
        ChangeRootPathCommand.NotifyCanExecuteChanged();
        OpenMatterLinkCommand.NotifyCanExecuteChanged();
        DiscoverMediaCommand.NotifyCanExecuteChanged();
        AddMediaCommand.NotifyCanExecuteChanged();
    }
}

using System.ComponentModel;
using System.Windows;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.App.ViewModels.Shell;
using Accession.Core.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly InventoryHost _host;
    private readonly InventoryWorkflows _workflows;
    private readonly IDialogService _dialogs;
    private readonly MediaWorkflows _media;

    public MainWindowViewModel(
        INavigationService navigation,
        InventoryHost host,
        InventoryWorkflows workflows,
        MediaWorkflows media,
        IDialogService dialogs,
        BusyTracker busy)
    {
        _navigation = navigation;
        _host = host;
        _workflows = workflows;
        _media = media;
        _dialogs = dialogs;
        Busy = busy;
        _navigation.CurrentChanged += (_, _) => OnPropertyChanged(nameof(CurrentScreen));
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

    public bool HasSession => _host.HasSession;

    public bool HasMatterUrl => !string.IsNullOrWhiteSpace(_host.Config?.MatterUrl);

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    public void Initialize() => ShowScreenForSession();

    /// <summary>Called when the main window is closing: closes the inventory (audit + release lock).</summary>
    public void OnClosing() => _workflows.CloseInventory();

    [RelayCommand]
    private Task NewInventory() => _workflows.NewInventoryAsync();

    [RelayCommand]
    private Task OpenInventory() => _workflows.OpenInventoryAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void CloseInventory() => _workflows.CloseInventory();

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
    private static void Exit() => Application.Current.MainWindow?.Close();

    /// <summary>Debug-only menu item to verify global error handling.</summary>
    [RelayCommand]
    private static void ThrowTestException() =>
        throw new InvalidOperationException("Test exception from the Help menu (debug builds only).");

    private bool CanModify() => _host.CanModify;

    private bool CanDiscover() => _media.CanDiscover;

    private bool CanAddMedia() => _media.CanAddMedia;

    private void ShowScreenForSession()
    {
        if (_host.HasSession)
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

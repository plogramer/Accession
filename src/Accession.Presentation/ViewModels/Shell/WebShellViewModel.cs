using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Accession.Data.Browsing;
using Accession.Data.Schema;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.Presentation.ViewModels.MediaScreen;
using Accession.Presentation.ViewModels.Scanning;
using Accession.UI.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels.Shell;

/// <summary>
/// Main window content while an inventory is open, drawn by the web UI (preview). Screens that have not moved
/// to the web UI yet open in the classic shell.
/// </summary>
public sealed partial class WebShellViewModel : ViewModelBase, IShellModel
{
    private readonly InventoryHost _host;
    private readonly ScanHost _scans;
    private readonly MediaWorkflows _media;
    private readonly MainWindowViewModel _main;
    private readonly FileBrowserNavigator _navigator;
    private readonly DashboardViewModel _dashboard;
    private readonly MediaListViewModel _mediaList;
    private readonly WebFilesViewModel _files;
    private readonly ScanQueueViewModel _scanQueue;
    private readonly ErrorsViewModel _errors;
    private readonly CategoriesViewModel _categories;
    private readonly AuditLogViewModel _auditLog;
    private readonly ShellNavItem _mediaItem;
    private readonly ShellNavItem _filesItem;
    private readonly ShellNavItem _queueItem;
    private readonly ShellNavItem _errorsItem;

    public WebShellViewModel(
        InventoryHost host,
        ScanHost scans,
        MediaWorkflows media,
        MainWindowViewModel main,
        FileBrowserNavigator navigator,
        DashboardViewModel dashboard,
        MediaListViewModel mediaList,
        WebFilesViewModel files,
        ScanQueueViewModel scanQueue,
        ErrorsViewModel errors,
        CategoriesViewModel categories,
        AuditLogViewModel auditLog)
    {
        _categories = categories;
        _auditLog = auditLog;
        _scanQueue = scanQueue;
        _errors = errors;
        _files = files;
        _mediaList = mediaList;
        _mediaItem = new ShellNavItem("Media", "media", "Inventory", mediaList);
        _host = host;
        _scans = scans;
        _media = media;
        _main = main;
        _navigator = navigator;
        _dashboard = dashboard;

        _filesItem = new ShellNavItem("Files", "files", "Inventory", files);
        _queueItem = new ShellNavItem("Scan Queue", "queue", "Scanning", scanQueue);
        _errorsItem = new ShellNavItem("Errors", "errors", "Scanning", errors);
        NavItems =
        [
            new ShellNavItem("Dashboard", "dashboard", "Overview", dashboard),
            _mediaItem,
            _filesItem,
            new ShellNavItem("Categories", "categories", "Inventory", categories),
            _queueItem,
            _errorsItem,
            new ShellNavItem("Audit Log", "audit", "Records", auditLog),
        ];
        SelectedItem = NavItems[0];
        UpdateBadges();
    }

    // ---- Inventory ----

    public string ClientName => _host.Config?.ClientName ?? string.Empty;

    public string MatterName => _host.Config?.MatterName ?? string.Empty;

    public string MatterCode => _host.Config?.MatterCode ?? string.Empty;

    public string UserName => _host.Session?.UserName ?? Environment.UserName;

    public string RootPath => _host.Config?.RootPath ?? string.Empty;

    public string SchemaText => _host.Config is { } config ? $"Schema v{config.SchemaVersion}" : string.Empty;

    public string LockStatus => _host.Session switch
    {
        null => string.Empty,
        { IsReadOnly: true } => "Read-only",
        _ => "Locked by you",
    };

    public bool IsReadOnly => _host.IsReadOnly;

    public bool IsOffline => _host.HasSession && !_host.IsRootAvailable;

    public string Notice => _host.Notice;

    // ---- Scan ----

    public string ScanStatus => _scans.StatusText;

    public bool IsScanActive => _scans.IsBusy;

    // ---- Navigation ----

    public IReadOnlyList<ShellNavItem> NavItems { get; }

    [ObservableProperty]
    public partial ShellNavItem SelectedItem { get; set; }

    // ---- Commands (dialog-based ones stay in the main window view model) ----

    public ICommand PauseScanCommand => _main.PauseScanCommand;

    public ICommand ResumeScanCommand => _main.ResumeScanCommand;

    public ICommand CancelScanCommand => _main.CancelScanCommand;

    public ICommand AddMediaCommand => _main.AddMediaCommand;

    public ICommand DiscoverMediaCommand => _main.DiscoverMediaCommand;

    public ICommand ShowPropertiesCommand => _main.ShowPropertiesCommand;

    public ICommand ChangeRootPathCommand => _main.ChangeRootPathCommand;

    public ICommand OpenMatterLinkCommand => _main.OpenMatterLinkCommand;

    public ICommand OpenSettingsCommand => _main.OpenSettingsCommand;

    public ICommand CloseInventoryCommand => _main.CloseInventoryCommand;

    ICommand IShellModel.ScanNowCommand => ScanNowCommand;

    ICommand IShellModel.DismissNoticeCommand => DismissNoticeCommand;

    ICommand IShellModel.OpenInClassicCommand => OpenInClassicCommand;

    public override void OnNavigatedTo()
    {
        _navigator.ShowFilesRequested += OnShowFiles;
        _host.PropertyChanged += OnSourceChanged;
        _scans.PropertyChanged += OnSourceChanged;
        _mediaList.RowsReloaded += OnMediaReloaded;
        _errors.PropertyChanged += OnErrorsChanged;
    }

    public override void OnNavigatedFrom()
    {
        _navigator.ShowFilesRequested -= OnShowFiles;
        _host.PropertyChanged -= OnSourceChanged;
        _scans.PropertyChanged -= OnSourceChanged;
        _dashboard.Dispose();
        _mediaList.RowsReloaded -= OnMediaReloaded;
        _mediaList.Dispose();
        _files.Dispose();
        _scanQueue.Dispose();
        _errors.PropertyChanged -= OnErrorsChanged;
        _errors.Dispose();
        _categories.Dispose();
        _auditLog.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanScanNow))]
    private void ScanNow() => _media.ScanPendingMedia();

    [RelayCommand]
    private void DismissNotice() => _host.SetNotice(string.Empty);

    /// <summary>Opens the selected screen in the classic shell, carrying a pending Files filter.</summary>
    [RelayCommand]
    private void OpenInClassic()
    {
        _main.SwitchToClassic(SelectedItem.Key, null);
    }

    private bool CanScanNow() => _scans.CanScan;

    private void OnShowFiles(object? sender, FileFilter filter)
    {
        SelectedItem = _filesItem;
        _files.ApplyPreset(filter);
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateBadges();
        ScanNowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(string.Empty);
    }

    private void OnMediaReloaded(object? sender, EventArgs e) => UpdateBadges();

    private void OnErrorsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ErrorsViewModel.ErrorCount))
        {
            UpdateBadges();
        }
    }

    private void UpdateBadges()
    {
        _mediaItem.Badge = _mediaList.Rows.Count > 0 ? _mediaList.Rows.Count.ToString(CultureInfo.CurrentCulture) : string.Empty;
        _queueItem.Badge = _scans.QueueLength > 0 ? _scans.QueueLength.ToString(CultureInfo.CurrentCulture) : string.Empty;
        _errorsItem.Badge = _errors.ErrorCount > 0 ? _errors.ErrorCount.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
    }

}

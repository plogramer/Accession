using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Accession.Data.Browsing;
using Accession.Data.Schema;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Dashboard;
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
    private readonly ShellNavItem _filesItem;
    private readonly ShellNavItem _queueItem;
    private readonly ShellNavItem _errorsItem;
    private FileFilter? _pendingFilter;

    public WebShellViewModel(
        InventoryHost host,
        ScanHost scans,
        MediaWorkflows media,
        MainWindowViewModel main,
        FileBrowserNavigator navigator,
        DashboardViewModel dashboard)
    {
        _host = host;
        _scans = scans;
        _media = media;
        _main = main;
        _navigator = navigator;
        _dashboard = dashboard;

        _filesItem = new ShellNavItem("Files", "files", "Inventory");
        _queueItem = new ShellNavItem("Scan Queue", "queue", "Scanning");
        _errorsItem = new ShellNavItem("Errors", "errors", "Scanning");
        NavItems =
        [
            new ShellNavItem("Dashboard", "dashboard", "Overview", dashboard),
            new ShellNavItem("Media", "media", "Inventory"),
            _filesItem,
            new ShellNavItem("Categories", "categories", "Inventory"),
            _queueItem,
            _errorsItem,
            new ShellNavItem("Audit Log", "audit", "Records"),
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

    public string PendingFilterText => _pendingFilter is { } filter && SelectedItem == _filesItem ? Describe(filter) : string.Empty;

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
        _dashboard.PropertyChanged += OnDashboardChanged;
    }

    public override void OnNavigatedFrom()
    {
        _navigator.ShowFilesRequested -= OnShowFiles;
        _host.PropertyChanged -= OnSourceChanged;
        _scans.PropertyChanged -= OnSourceChanged;
        _dashboard.PropertyChanged -= OnDashboardChanged;
        _dashboard.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanScanNow))]
    private void ScanNow() => _media.ScanPendingMedia();

    [RelayCommand]
    private void DismissNotice() => _host.SetNotice(string.Empty);

    /// <summary>Opens the selected screen in the classic shell, carrying a pending Files filter.</summary>
    [RelayCommand]
    private void OpenInClassic()
    {
        var screen = SelectedItem.IsAvailable ? null : SelectedItem.Key;
        var filter = SelectedItem == _filesItem ? _pendingFilter : null;
        _main.SwitchToClassic(screen, filter);
    }

    private bool CanScanNow() => _scans.CanScan;

    partial void OnSelectedItemChanged(ShellNavItem value)
    {
        if (value != _filesItem)
        {
            _pendingFilter = null;
        }

        OnPropertyChanged(nameof(PendingFilterText));
    }

    private void OnShowFiles(object? sender, FileFilter filter)
    {
        _pendingFilter = filter;
        SelectedItem = _filesItem;
        OnPropertyChanged(nameof(PendingFilterText));
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateBadges();
        ScanNowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(string.Empty);
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.ErrorCount))
        {
            UpdateBadges();
        }
    }

    private void UpdateBadges()
    {
        _queueItem.Badge = _scans.QueueLength > 0 ? _scans.QueueLength.ToString(CultureInfo.CurrentCulture) : string.Empty;
        _errorsItem.Badge = _dashboard.ErrorCount is "0" or "—" ? string.Empty : _dashboard.ErrorCount;
    }

    /// <summary>Short text for a Files filter, e.g. "Media 123-123_001 · Email".</summary>
    private string Describe(FileFilter filter)
    {
        var parts = new List<string>();
        if (filter.MediaKey is { } key)
        {
            parts.Add("Media " + (_dashboard.MediaFilter.FirstOrDefault(m => m.MediaKey == key)?.MediaId ?? key.ToString(CultureInfo.InvariantCulture)));
        }

        if (filter.CategoryId is { } categoryId)
        {
            parts.Add(CategoryCatalog.Categories.FirstOrDefault(c => c.CategoryId == categoryId)?.Name ?? "category " + categoryId);
        }

        if (filter.Extension is { } extension)
        {
            parts.Add(extension.Length == 0 ? "no extension" : "." + extension);
        }

        if (filter.ModifiedFrom is { } from)
        {
            parts.Add("modified in " + from.Year.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(filter.NameContains))
        {
            parts.Add($"name “{filter.NameContains}”");
        }

        if (filter.DuplicatesOnly)
        {
            parts.Add("duplicates only");
        }

        return parts.Count == 0 ? "all files" : string.Join(" · ", parts);
    }
}

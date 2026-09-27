using System.ComponentModel;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.App.ViewModels.MediaScreen;
using Accession.App.ViewModels.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels.Shell;

/// <summary>Main window content while an inventory is open (requirements 8.4): navigation, screen, status bar.</summary>
public sealed partial class InventoryShellViewModel : ViewModelBase
{
    private readonly InventoryHost _host;

    private readonly MediaListViewModel _mediaList;
    private readonly NavItem _mediaItem;
    private readonly ScanHost _scans;
    private readonly MediaWorkflows _media;

    private readonly NavItem _scanQueueItem;
    private readonly NavItem _errorsItem;
    private readonly ErrorsViewModel _errors;

    public InventoryShellViewModel(
        InventoryHost host,
        MediaListViewModel mediaList,
        ScanQueueViewModel scanQueue,
        ErrorsViewModel errors,
        ScanHost scans,
        MediaWorkflows media)
    {
        _host = host;
        _scans = scans;
        _media = media;
        _errors = errors;
        _scanQueueItem = new NavItem("Scan Queue", scanQueue);
        _errorsItem = new NavItem("Errors", errors);
        _mediaList = mediaList;
        _mediaItem = new NavItem("Media", mediaList);
        NavItems =
        [
            new NavItem("Dashboard", new PlaceholderViewModel("Dashboard", "Totals by media, category and extension.", "Dashboard epic (#7)")),
            _mediaItem,
            new NavItem("Files", new PlaceholderViewModel("Files", "Browse and filter every inventoried file.", "File browser epic (#8)")),
            _scanQueueItem,
            _errorsItem,
            new NavItem("Categories", new PlaceholderViewModel("Categories", "File categories and their extensions.", "File browser epic (#8)")),
            new NavItem("Audit Log", new PlaceholderViewModel("Audit Log", "Who did what and when.", "File browser epic (#8)")),
        ];
        SelectedItem = NavItems[0];
        _mediaList.RowsReloaded += (_, _) => UpdateMediaBadge();
        _errors.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ErrorsViewModel.ErrorCount))
            {
                UpdateBadges();
            }
        };
        UpdateMediaBadge();
        UpdateBadges();
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem SelectedItem { get; set; }

    public string RootPath => _host.Config?.RootPath ?? string.Empty;

    public string LockStatus => _host.Session switch
    {
        null => string.Empty,
        { IsReadOnly: true } => "READ-ONLY",
        _ => "Locked by you",
    };

    public string SchemaText => _host.Config is { } config ? $"Schema v{config.SchemaVersion}" : string.Empty;

    public bool IsOffline => _host.HasSession && !_host.IsRootAvailable;

    public string Notice => _host.Notice;

    public string ScanStatus => _scans.StatusText;

    public bool HasScanStatus => !string.IsNullOrEmpty(_scans.StatusText);

    public bool CanScanNow => _scans.CanScan;

    public bool HasNotice => !string.IsNullOrEmpty(_host.Notice);

    public override void OnNavigatedTo()
    {
        _host.PropertyChanged += OnHostChanged;
        _scans.PropertyChanged += OnHostChanged;
    }

    public override void OnNavigatedFrom()
    {
        _host.PropertyChanged -= OnHostChanged;
        _scans.PropertyChanged -= OnHostChanged;
        foreach (var disposable in NavItems.Select(n => n.Content).OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }

    [RelayCommand]
    private void DismissNotice() => _host.SetNotice(string.Empty);

    [RelayCommand]
    private void ShowMedia() => SelectedItem = _mediaItem;

    [RelayCommand]
    private void ScanNow() => _media.ScanPendingMedia();

    private void UpdateBadges()
    {
        _scanQueueItem.Badge = _scans.QueueLength > 0 ? _scans.QueueLength.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        _errorsItem.Badge = _errors.ErrorCount > 0 ? _errors.ErrorCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
    }

    private void UpdateMediaBadge() =>
        _mediaItem.Badge = _mediaList.Rows.Count > 0 ? _mediaList.Rows.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateBadges();
        OnPropertyChanged(string.Empty);
    }
}

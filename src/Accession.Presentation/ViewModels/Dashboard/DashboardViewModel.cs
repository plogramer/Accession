using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Queries;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.MediaScreen;
using Accession.UI.Dashboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Accession.UI.MediaScreen;
using Accession.UI.Shell;

namespace Accession.Presentation.ViewModels.Dashboard;

/// <summary>Dashboard (requirements 5.7, section 8.5). Fast sections load first; File-table queries load in the background.</summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDashboardModel, IDisposable
{
    private readonly IUiDispatcher _ui;

    public const double MaxBarLength = DashboardScale.MaxBarLength;

    private readonly InventoryHost _host;
    private readonly ScanHost _scans;
    private readonly DashboardQueries _queries;
    private readonly ISettingsService _settings;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly FileBrowserNavigator _navigator;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _reloadDelay;
    private IReadOnlyList<CategoryTotal> _categoryTotals = [];
    private CancellationTokenSource? _loadCancel;
    private IReadOnlyList<ExtensionRowVm> _allExtensions = [];
    private bool _updatingFilter;
    private bool? _lastScanBusy;
    private (SizeUnitSystem Unit, DisplayTimeZone Zone) _displaySettings;

    /// <summary>Wait after a selection change before reloading, so ticking several media reloads once.</summary>
    public static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    public DashboardViewModel(InventoryHost host, ScanHost scans, DashboardQueries queries, ISettingsService settings,
        FileBrowserNavigator navigator, ILogger<DashboardViewModel> logger,
        IUiDispatcher ui, TimeProvider time, DashboardCache? cache = null)
    {
        _cache = cache;
        _ui = ui;
        _time = time;
        _navigator = navigator;
        _host = host;
        _scans = scans;
        _queries = queries;
        _settings = settings;
        _logger = logger;
        _host.MediaChanged += OnMediaChanged;
        _scans.PropertyChanged += OnScansChanged;
        _settings.SettingsChanged += OnSettingsChanged;
        _displaySettings = (settings.Current.SizeUnit, settings.Current.DisplayTimeZone);
        LoadMediaFilter();
        _ = ReloadAsync();
    }

    private readonly DashboardCache? _cache;

    /// <summary>The latest dashboard load (for tests).</summary>
    internal Task LastLoad { get; private set; } = Task.CompletedTask;

    // ---- Filter ----

    public ObservableCollection<MediaFilterItem> MediaFilter { get; } = [];

    [ObservableProperty]
    public partial bool IsFilterOpen { get; set; }

    public string FilterText
    {
        get
        {
            var selected = MediaFilter.Count(m => m.IsChecked);
            return selected == MediaFilter.Count ? "All media"
                : selected == 0 ? "No media selected"
                : selected == 1 ? MediaFilter.First(m => m.IsChecked).MediaId
                : $"{selected} of {MediaFilter.Count} media";
        }
    }

    /// <summary>Narrows the filter list by Media ID; Select all / Unselect all apply to the media shown.</summary>
    [ObservableProperty]
    public partial string MediaSearch { get; set; } = string.Empty;

    public IReadOnlyList<MediaFilterItem> VisibleMediaFilter
    {
        get
        {
            var search = MediaSearch.Trim();
            return search.Length == 0
                ? MediaFilter
                : [.. MediaFilter.Where(m => m.MediaId.Contains(search, StringComparison.OrdinalIgnoreCase))];
        }
    }

    public string SelectionSummary => $"{MediaFilter.Count(m => m.IsChecked):N0} of {MediaFilter.Count:N0} selected";

    public bool AllMediaSelected => MediaFilter.All(m => m.IsChecked);

    public bool NoMediaSelected => MediaFilter.Count > 0 && MediaFilter.All(m => !m.IsChecked);

    /// <summary>Subtitle of the Media tile.</summary>
    public string MediaNote => AllMediaSelected ? "in this inventory" : $"selected of {MediaFilter.Count:N0}";

    // ---- State ----

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDetails { get; set; }

    public bool ScanInProgress => _scans.IsBusy;

    [ObservableProperty]
    public partial string LoadError { get; set; } = string.Empty;

    // ---- Tiles ----

    [ObservableProperty]
    public partial string MediaCount { get; set; } = "—";

    [ObservableProperty]
    public partial string FolderCount { get; set; } = "—";

    [ObservableProperty]
    public partial string FileCount { get; set; } = "—";

    [ObservableProperty]
    public partial string TotalSize { get; set; } = "—";

    [ObservableProperty]
    public partial string HashedPercent { get; set; } = "—";

    [ObservableProperty]
    public partial string UniqueFiles { get; set; } = "—";

    [ObservableProperty]
    public partial string DuplicateFiles { get; set; } = "—";

    [ObservableProperty]
    public partial string DuplicateSize { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DuplicateNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorCount { get; set; } = "—";

    // ---- Sections ----

    public ObservableCollection<DashboardMediaRowVm> ByMedia { get; } = [];

    public ObservableCollection<BarRow> ByCategory { get; } = [];

    [ObservableProperty]
    public partial BarRow? SelectedCategory { get; set; }

    public ObservableCollection<ExtensionRowVm> ByExtension { get; } = [];

    [ObservableProperty]
    public partial string ExtensionSearch { get; set; } = string.Empty;

    public string ExtensionHeader => SelectedCategory is { } c ? $"By Extension – {c.Label}" : "By Extension";

    public ObservableCollection<BarRow> ByYear { get; } = [];

    public ObservableCollection<LargeFileRowVm> LargestFiles { get; } = [];

    ICommand IRefreshableScreen.RefreshCommand => RefreshCommand;
    ICommand IDashboardModel.SelectAllMediaCommand => SelectAllMediaCommand;
    ICommand IDashboardModel.UnselectAllMediaCommand => UnselectAllMediaCommand;
    ICommand IDashboardModel.SelectOnlyMediaCommand => SelectOnlyMediaCommand;
    ICommand IDashboardModel.ToggleAllMediaCommand => ToggleAllMediaCommand;
    ICommand IDashboardModel.ClearCategoryCommand => ClearCategoryCommand;
    ICommand IDashboardModel.OpenMediaCommand => OpenMediaCommand;
    ICommand IDashboardModel.OpenCategoryCommand => OpenCategoryCommand;
    ICommand IDashboardModel.OpenExtensionCommand => OpenExtensionCommand;
    ICommand IDashboardModel.OpenYearCommand => OpenYearCommand;
    ICommand IDashboardModel.OpenLargeFileCommand => OpenLargeFileCommand;
    ICommand IDashboardModel.OpenDuplicatesCommand => OpenDuplicatesCommand;

    public void Dispose()
    {
        _host.MediaChanged -= OnMediaChanged;
        _scans.PropertyChanged -= OnScansChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        _reloadDelay?.Cancel();
        _loadCancel?.Cancel();
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync();

    /// <summary>Selects the media shown in the filter list (all of them unless searching).</summary>
    [RelayCommand]
    private void SelectAllMedia() => SetMedia(VisibleMediaFilter, true);

    /// <summary>Unselects the media shown in the filter list (all of them unless searching).</summary>
    [RelayCommand]
    private void UnselectAllMedia() => SetMedia(VisibleMediaFilter, false);

    [RelayCommand]
    private void SelectOnlyMedia(MediaFilterItem? item)
    {
        if (item is not null)
        {
            SetMedia(MediaFilter, false, item);
        }
    }

    /// <summary>The By media table's header checkbox: everything, or nothing when everything is selected.</summary>
    [RelayCommand]
    private void ToggleAllMedia() => SetMedia(MediaFilter, !AllMediaSelected);

    [RelayCommand]
    private void ClearCategory() => SelectedCategory = null;

    // ---- Click-through to the File browser (DSH-09) ----

    [RelayCommand]
    private void OpenMedia(DashboardMediaRowVm? row)
    {
        if (row is not null)
        {
            _navigator.ShowFiles(new FileFilter { MediaKey = row.MediaKey });
        }
    }

    [RelayCommand]
    private void OpenCategory(BarRow? row)
    {
        if (row is not null && _categoryTotals.FirstOrDefault(c => c.Category == row.Label) is { } category)
        {
            _navigator.ShowFiles(WithMedia(new FileFilter { CategoryId = category.CategoryId }));
        }
    }

    [RelayCommand]
    private void OpenExtension(ExtensionRowVm? row)
    {
        if (row is not null)
        {
            _navigator.ShowFiles(WithMedia(new FileFilter { Extension = row.Extension == "(none)" ? string.Empty : row.Extension }));
        }
    }

    [RelayCommand]
    private void OpenYear(BarRow? row)
    {
        if (row is not null && int.TryParse(row.Label, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            _navigator.ShowFiles(WithMedia(new FileFilter
            {
                ModifiedFrom = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ModifiedTo = new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }));
        }
    }

    [RelayCommand]
    private void OpenLargeFile(LargeFileRowVm? row)
    {
        if (row is not null)
        {
            var name = row.RelativePath[(row.RelativePath.LastIndexOf('\\') + 1)..];
            _navigator.ShowFiles(new FileFilter { MediaKey = row.MediaKey, NameContains = name, MinSize = row.SizeBytes, MaxSize = row.SizeBytes });
        }
    }

    [RelayCommand]
    private void OpenDuplicates() => _navigator.ShowFiles(WithMedia(new FileFilter { DuplicatesOnly = true }));

    /// <summary>Carries the media selection into the File browser filter.</summary>
    private FileFilter WithMedia(FileFilter filter)
    {
        if (AllMediaSelected)
        {
            return filter;
        }

        var selected = MediaFilter.Where(m => m.IsChecked).Select(m => m.MediaKey).ToList();
        return selected.Count == 1 ? filter with { MediaKey = selected[0] } : filter with { MediaKeys = selected };
    }

    partial void OnSelectedCategoryChanged(BarRow? value)
    {
        OnPropertyChanged(nameof(ExtensionHeader));
        ApplyExtensionFilter();
    }

    partial void OnExtensionSearchChanged(string value) => ApplyExtensionFilter();

    /// <summary>Sets <paramref name="items"/> to <paramref name="isChecked"/> (and <paramref name="except"/> to the opposite), then reloads once.</summary>
    private void SetMedia(IReadOnlyList<MediaFilterItem> items, bool isChecked, MediaFilterItem? except = null)
    {
        _updatingFilter = true;
        foreach (var item in items)
        {
            item.IsChecked = ReferenceEquals(item, except) ? !isChecked : isChecked;
        }

        _updatingFilter = false;
        OnFilterChanged();
    }

    partial void OnMediaSearchChanged(string value) => OnPropertyChanged(nameof(VisibleMediaFilter));

    private void LoadMediaFilter()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        var previouslyUnchecked = MediaFilter.Where(m => !m.IsChecked).Select(m => m.MediaKey).ToHashSet();
        foreach (var item in MediaFilter)
        {
            item.PropertyChanged -= OnFilterItemChanged;
        }

        MediaFilter.Clear();
        try
        {
            foreach (var row in _queries.ByMedia(session.Database, DashboardFilter.All))
            {
                var item = new MediaFilterItem(row.MediaKey, row.MediaId, !previouslyUnchecked.Contains(row.MediaKey));
                item.PropertyChanged += OnFilterItemChanged;
                MediaFilter.Add(item);
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading the media filter failed");
        }

        OnSelectionChanged();
        OnPropertyChanged(nameof(VisibleMediaFilter));
    }

    private void OnFilterItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_updatingFilter)
        {
            OnFilterChanged();
        }
    }

    private void OnFilterChanged()
    {
        OnSelectionChanged();
        LastLoad = ReloadAfterDelayAsync();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllMediaSelected));
        OnPropertyChanged(nameof(NoMediaSelected));
        OnPropertyChanged(nameof(MediaNote));
    }

    private async Task ReloadAfterDelayAsync()
    {
        _reloadDelay?.Cancel();
        var delay = new CancellationTokenSource();
        _reloadDelay = delay;
        try
        {
            await Task.Delay(ReloadDelay, _time, delay.Token);
        }
        catch (OperationCanceledException)
        {
            return; // another change came in: that one reloads
        }

        await ReloadAsync();
    }

    /// <summary>The query filter, or null when no media is selected.</summary>
    private DashboardFilter? CurrentFilter() =>
        AllMediaSelected ? DashboardFilter.All
        : NoMediaSelected ? null
        : new DashboardFilter(MediaFilter.Where(m => m.IsChecked).Select(m => m.MediaKey).ToList());

    private Task ReloadAsync()
    {
        _reloadDelay?.Cancel();
        var load = ReloadCoreAsync();
        LastLoad = load;
        return load;
    }

    private async Task ReloadCoreAsync()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        _loadCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _loadCancel = cancel;
        var token = cancel.Token;
        var filter = CurrentFilter();
        var database = session.Database;
        var unit = _settings.Current.SizeUnit;
        var zone = _settings.Current.DisplayTimeZone;
        LoadError = string.Empty;

        // Fast part: Media totals and extension summaries (DSH-11). The By media table always lists every media,
        // so media can be ticked there; everything else follows the selection.
        IsLoading = true;
        try
        {
            var fast = await Task.Run(() => (
                Summary: filter is null ? null : _queries.Summary(database, filter, token),
                Media: _queries.ByMedia(database, DashboardFilter.All, token),
                Categories: filter is null ? [] : _queries.ByCategory(database, filter, token),
                Extensions: filter is null ? [] : _queries.ByExtension(database, filter, token)), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            ShowFast(fast.Summary, fast.Media, fast.Categories, fast.Extensions, unit, zone);
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Loading the dashboard failed");
            LoadError = $"The dashboard could not be loaded: {ex.Message}";
            return;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }

        if (filter is null)
        {
            ClearSlow(string.Empty);
            return;
        }

        // Background part: File-table queries. Skipped while a scan runs: long reads would make the scan's
        // database writer wait (rollback-journal mode, required on network shares).
        if (_scans.IsBusy)
        {
            DuplicateNote = "available when the scan finishes";
            UniqueFiles = DuplicateFiles = "—";
            DuplicateSize = string.Empty;
            foreach (var row in ByMedia)
            {
                row.Duplicates = "—";
            }

            return;
        }

        IsLoadingDetails = true;
        DuplicateNote = "calculating…";
        try
        {
            var inventory = session.Config.InventoryGuid;
            var slow = await Task.Run(() =>
            {
                // Duplicates and years read every file (tens of seconds on millions of files): use the results saved on this
                // computer while the inventory's data is unchanged.
                var version = _cache is null ? null : DashboardCache.DataVersion(database, _queries);
                var saved = version is null ? null : _cache!.Get(inventory, version, filter);
                var sections = saved ?? new SlowDashboard(
                    _queries.Duplicates(database, filter, token),
                    _queries.DuplicatesByMedia(database, filter, token),
                    _queries.ByYear(database, filter, token));
                if (saved is null && version is not null && !token.IsCancellationRequested)
                {
                    _cache!.Put(inventory, version, filter, sections);
                }

                return (Sections: sections, Largest: _queries.LargestFiles(database, filter, token));
            }, token);
            if (!token.IsCancellationRequested)
            {
                ShowSlow(slow.Sections.Duplicates, slow.Sections.PerMedia, slow.Sections.Years, slow.Largest, unit, zone);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (Accession.Data.LongReads.IsInterrupted(ex))
        {
            // Something was written meanwhile (media added, scan queued): the long queries gave way. Try again shortly.
            _ = RetrySlowAsync(token); // its timer starts now, before anyone sees the note below
            DuplicateNote = "paused while the inventory changes…";
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Loading dashboard details failed");
            DuplicateNote = "could not be calculated";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoadingDetails = false;
            }
        }
    }

    /// <summary>After the long queries gave way to a write, reload once things are quiet (unless something reloaded already).</summary>
    public static readonly TimeSpan RetryAfterInterrupt = TimeSpan.FromSeconds(3);

    private async Task RetrySlowAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(RetryAfterInterrupt, _time, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await ReloadAsync();
        }
    }

    private void ShowFast(DashboardSummary? s, IReadOnlyList<DashboardMediaRow> media, IReadOnlyList<CategoryTotal> categories,
        IReadOnlyList<ExtensionTotal> extensions, SizeUnitSystem unit, DisplayTimeZone zone)
    {
        var culture = CultureInfo.CurrentCulture;
        MediaCount = s?.MediaCount.ToString("N0", culture) ?? "0";
        FolderCount = s?.FolderCount.ToString("N0", culture) ?? "0";
        FileCount = s?.FileCount.ToString("N0", culture) ?? "—";
        TotalSize = s is null ? "—" : SizeFormatter.Format(s.TotalBytes, unit);
        HashedPercent = s is null || s.FileCount == 0 ? "—" : $"{Math.Floor(1000d * s.HashedCount / s.FileCount) / 10:0.0} %";
        ErrorCount = s?.ErrorCount.ToString("N0", culture) ?? "—";

        var selection = MediaFilter.ToDictionary(m => m.MediaKey);
        ByMedia.Clear();
        foreach (var m in media)
        {
            var scanned = m.ScanCount > 0 || m.FileCount > 0;
            ByMedia.Add(new DashboardMediaRowVm
            {
                Selection = selection.GetValueOrDefault(m.MediaKey),
                MediaKey = m.MediaKey,
                MediaId = m.MediaId,
                Status = MediaRowViewModel.Humanize(m.Status),
                Folders = scanned ? m.FolderCount.ToString("N0", culture) : "—",
                Files = scanned ? m.FileCount.ToString("N0", culture) : "—",
                Size = scanned ? SizeFormatter.Format(m.TotalBytes, unit) : "—",
                Hashed = !scanned ? "—" : m.FileCount == 0 ? "100 %" : $"{Math.Floor(100d * m.HashedCount / m.FileCount):0} %",
                Errors = scanned ? m.ErrorCount.ToString("N0", culture) : "—",
                LastScanned = m.LastScanCompletedUtc is { } t ? TimeFormatter.Format(t, zone) : "never",
                Scans = m.ScanCount,
                FileCount = m.FileCount,
                TotalBytes = m.TotalBytes,
                HashedRatio = !scanned ? 0 : m.FileCount == 0 ? 1 : (double)m.HashedCount / m.FileCount,
                Duplicates = selection.GetValueOrDefault(m.MediaKey) is { IsChecked: true } ? "…" : "—",
            });
        }

        _categoryTotals = categories;
        if (categories.Count == 0)
        {
            ByCategory.Clear();
            _allExtensions = [];
            SelectedCategory = null;
            ApplyExtensionFilter();
            return;
        }

        var totalFiles = Math.Max(1, categories.Sum(c => c.FileCount));
        var totalBytes = Math.Max(1, categories.Sum(c => c.TotalBytes));
        var maxBytes = Math.Max(1, categories.Max(c => c.TotalBytes));
        var selected = SelectedCategory?.Label;
        ByCategory.Clear();
        foreach (var c in categories.Where(c => c.FileCount > 0))
        {
            ByCategory.Add(new BarRow(c.Category, c.FileCount.ToString("N0", culture), SizeFormatter.Format(c.TotalBytes, unit),
                Percent(c.FileCount, totalFiles), Percent(c.TotalBytes, totalBytes), MaxBarLength * c.TotalBytes / maxBytes, c.FileCount, c.TotalBytes));
        }

        var extensionBytes = Math.Max(1, extensions.Sum(e => e.TotalBytes));
        _allExtensions = extensions.Select(e => new ExtensionRowVm(
            e.Extension.Length == 0 ? "(none)" : e.Extension, e.Category, e.FileCount.ToString("N0", culture),
            SizeFormatter.Format(e.TotalBytes, unit), Percent(e.TotalBytes, extensionBytes), e.FileCount, e.TotalBytes)).ToList();
        SelectedCategory = ByCategory.FirstOrDefault(c => c.Label == selected);
        ApplyExtensionFilter();
    }

    /// <summary>Empties the background sections (no media selected).</summary>
    private void ClearSlow(string note)
    {
        UniqueFiles = DuplicateFiles = "—";
        DuplicateSize = string.Empty;
        DuplicateNote = note;
        foreach (var row in ByMedia)
        {
            row.Duplicates = "—";
        }

        ByYear.Clear();
        LargestFiles.Clear();
    }

    private void ShowSlow(DuplicateSummary d, IReadOnlyList<MediaDuplicates> perMedia, IReadOnlyList<YearTotal> years,
        IReadOnlyList<LargeFile> largest, SizeUnitSystem unit, DisplayTimeZone zone)
    {
        var culture = CultureInfo.CurrentCulture;
        UniqueFiles = d.UniqueFiles.ToString("N0", culture);
        DuplicateFiles = d.DuplicateFiles.ToString("N0", culture);
        DuplicateSize = SizeFormatter.Format(d.DuplicateBytes, unit);
        DuplicateNote = d.NotHashedFiles > 0 ? $"{d.NotHashedFiles:N0} files not hashed yet are excluded" : "by SHA-1";

        var dupByMedia = perMedia.ToDictionary(p => p.MediaKey);
        foreach (var row in ByMedia)
        {
            row.Duplicates = dupByMedia.TryGetValue(row.MediaKey, out var dup) ? dup.DuplicateFiles.ToString("N0", culture) : "—";
        }

        var maxYearFiles = Math.Max(1, years.Count == 0 ? 1 : years.Max(y => y.FileCount));
        ByYear.Clear();
        foreach (var y in years)
        {
            ByYear.Add(new BarRow(y.Year, y.FileCount.ToString("N0", culture), SizeFormatter.Format(y.TotalBytes, unit),
                string.Empty, string.Empty, MaxBarLength * y.FileCount / maxYearFiles, y.FileCount, y.TotalBytes));
        }

        LargestFiles.Clear();
        foreach (var f in largest)
        {
            LargestFiles.Add(new LargeFileRowVm(f.MediaKey, f.MediaId, f.RelativePath, SizeFormatter.Format(f.SizeBytes, unit), TimeFormatter.Format(f.ModifiedUtc, zone), f.SizeBytes));
        }
    }

    private void ApplyExtensionFilter()
    {
        var search = ExtensionSearch.Trim().TrimStart('.');
        ByExtension.Clear();
        foreach (var row in _allExtensions.Where(e =>
                     (SelectedCategory is null || e.Category == SelectedCategory.Label) &&
                     (search.Length == 0 || e.Extension.Contains(search, StringComparison.OrdinalIgnoreCase))))
        {
            ByExtension.Add(row);
        }
    }

    private static string Percent(long part, long total) => $"{100d * part / total:0.0} %";

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        LoadMediaFilter();
        _ = ReloadAsync();
    }

    private void OnScansChanged(object? sender, PropertyChangedEventArgs e)
    {
        var wasScanning = ScanInProgress;
        OnPropertyChanged(nameof(ScanInProgress));
        _lastScanBusy ??= wasScanning;
        if (_lastScanBusy == true && !_scans.IsBusy)
        {
            _ = ReloadAsync(); // scan finished: load the sections that were skipped
        }

        _lastScanBusy = _scans.IsBusy;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // Only display settings affect the dashboard (not e.g. the web theme or saved grid layouts).
        var display = (e.Settings.SizeUnit, e.Settings.DisplayTimeZone);
        if (display != _displaySettings)
        {
            _displaySettings = display;
            _ui.Post(() => _ = ReloadAsync());
        }
    }
}

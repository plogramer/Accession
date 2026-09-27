using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.App.ViewModels.MediaScreen;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Queries;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.App.ViewModels.Dashboard;

/// <summary>Dashboard (requirements 5.7, section 8.5). Fast sections load first; File-table queries load in the background.</summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    public const double MaxBarLength = 220;

    private readonly InventoryHost _host;
    private readonly ScanHost _scans;
    private readonly DashboardQueries _queries;
    private readonly ISettingsService _settings;
    private readonly ILogger<DashboardViewModel> _logger;
    private CancellationTokenSource? _loadCancel;
    private IReadOnlyList<ExtensionRowVm> _allExtensions = [];
    private bool _updatingFilter;
    private bool? _lastScanBusy;

    public DashboardViewModel(InventoryHost host, ScanHost scans, DashboardQueries queries, ISettingsService settings, ILogger<DashboardViewModel> logger)
    {
        _host = host;
        _scans = scans;
        _queries = queries;
        _settings = settings;
        _logger = logger;
        _host.MediaChanged += OnMediaChanged;
        _scans.PropertyChanged += OnScansChanged;
        _settings.SettingsChanged += OnSettingsChanged;
        LoadMediaFilter();
        _ = ReloadAsync();
    }

    // ---- Filter ----

    public ObservableCollection<MediaFilterItem> MediaFilter { get; } = [];

    [ObservableProperty]
    public partial bool IsFilterOpen { get; set; }

    public string FilterText
    {
        get
        {
            var selected = MediaFilter.Count(m => m.IsChecked);
            return selected == MediaFilter.Count ? "All media" : selected == 1 ? MediaFilter.First(m => m.IsChecked).MediaId : $"{selected} of {MediaFilter.Count} media";
        }
    }

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

    public void Dispose()
    {
        _host.MediaChanged -= OnMediaChanged;
        _scans.PropertyChanged -= OnScansChanged;
        _settings.SettingsChanged -= OnSettingsChanged;
        _loadCancel?.Cancel();
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync();

    [RelayCommand]
    private void SelectAllMedia() => SetAllMedia(true);

    [RelayCommand]
    private void ClearCategory() => SelectedCategory = null;

    partial void OnSelectedCategoryChanged(BarRow? value)
    {
        OnPropertyChanged(nameof(ExtensionHeader));
        ApplyExtensionFilter();
    }

    partial void OnExtensionSearchChanged(string value) => ApplyExtensionFilter();

    private void SetAllMedia(bool isChecked)
    {
        _updatingFilter = true;
        foreach (var item in MediaFilter)
        {
            item.IsChecked = isChecked;
        }

        _updatingFilter = false;
        OnFilterChanged();
    }

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

        OnPropertyChanged(nameof(FilterText));
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
        OnPropertyChanged(nameof(FilterText));
        _ = ReloadAsync();
    }

    private DashboardFilter CurrentFilter() =>
        MediaFilter.All(m => m.IsChecked) ? DashboardFilter.All : new DashboardFilter(MediaFilter.Where(m => m.IsChecked).Select(m => m.MediaKey).ToList());

    private async Task ReloadAsync()
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

        // Fast part: Media totals and extension summaries (DSH-11).
        IsLoading = true;
        try
        {
            var fast = await Task.Run(() => (
                Summary: _queries.Summary(database, filter, token),
                Media: _queries.ByMedia(database, filter, token),
                Categories: _queries.ByCategory(database, filter, token),
                Extensions: _queries.ByExtension(database, filter, token)), token);
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
            var slow = await Task.Run(() => (
                Duplicates: _queries.Duplicates(database, filter, token),
                PerMedia: _queries.DuplicatesByMedia(database, filter, token),
                Years: _queries.ByYear(database, filter, token),
                Largest: _queries.LargestFiles(database, filter, token)), token);
            if (!token.IsCancellationRequested)
            {
                ShowSlow(slow.Duplicates, slow.PerMedia, slow.Years, slow.Largest, unit, zone);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
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

    private void ShowFast(DashboardSummary s, IReadOnlyList<DashboardMediaRow> media, IReadOnlyList<CategoryTotal> categories,
        IReadOnlyList<ExtensionTotal> extensions, SizeUnitSystem unit, DisplayTimeZone zone)
    {
        var culture = CultureInfo.CurrentCulture;
        MediaCount = s.MediaCount.ToString("N0", culture);
        FolderCount = s.FolderCount.ToString("N0", culture);
        FileCount = s.FileCount.ToString("N0", culture);
        TotalSize = SizeFormatter.Format(s.TotalBytes, unit);
        HashedPercent = s.FileCount == 0 ? "—" : $"{Math.Floor(1000d * s.HashedCount / s.FileCount) / 10:0.0} %";
        ErrorCount = s.ErrorCount.ToString("N0", culture);

        ByMedia.Clear();
        foreach (var m in media)
        {
            var scanned = m.ScanCount > 0 || m.FileCount > 0;
            ByMedia.Add(new DashboardMediaRowVm
            {
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
            });
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
            LargestFiles.Add(new LargeFileRowVm(f.MediaId, f.RelativePath, SizeFormatter.Format(f.SizeBytes, unit), TimeFormatter.Format(f.ModifiedUtc, zone), f.SizeBytes));
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

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => UiThread.Post(() => _ = ReloadAsync());
}

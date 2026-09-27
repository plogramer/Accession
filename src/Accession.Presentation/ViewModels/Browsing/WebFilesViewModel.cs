using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data;
using Accession.Data.Browsing;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.UI.Components;
using Accession.UI.FilesScreen;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Accession.UI.Shell;

namespace Accession.Presentation.ViewModels.Browsing;

/// <summary>
/// Files screen of the web UI (requirements 5.8, BRW-*): folder tree, filters and numbered pages of
/// 1,000–50,000 rows. Pages are read by <see cref="FilePager"/>, off the UI thread.
/// </summary>
public sealed partial class WebFilesViewModel : ViewModelBase, IFilesModel, IDisposable
{
    private static readonly IReadOnlyList<SelectOption> HashOptions =
    [
        new(string.Empty, "Any hash status"),
        new(nameof(HashStatus.Hashed), "Hashed"),
        new(nameof(HashStatus.Pending), "Not hashed yet"),
        new(nameof(HashStatus.Error), "Could not be read"),
        new(nameof(HashStatus.Skipped), "Skipped (link)"),
    ];

    private readonly InventoryHost _host;
    private readonly FileBrowserQueries _queries;
    private readonly CategoryQueries _categories;
    private readonly ISettingsService _settings;
    private readonly IDesktop _desktop;
    private readonly IDialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly ILogger<WebFilesViewModel> _logger;
    private FileFilter _activeFilter = FileFilter.None;
    private FilePager? _pager;
    private CancellationTokenSource? _cancel;
    private bool _suppressApply;
    private HashSet<long> _mediaKeys = [];
    private readonly ExportWorkflow? _export;
    private IReadOnlyList<SelectOption> _mediaRootOptions = [];
    private IReadOnlyList<long>? _mediaSet;

    /// <summary>Media option value for a set of media handed over by another screen.</summary>
    public const string MediaSetValue = "set";

    public WebFilesViewModel(InventoryHost host, FileBrowserQueries queries, CategoryQueries categories, ISettingsService settings,
        IDesktop desktop, IDialogService dialogs, ToastService toasts, ILogger<WebFilesViewModel> logger, ExportWorkflow? export = null)
    {
        _export = export;
        _host = host;
        _queries = queries;
        _categories = categories;
        _settings = settings;
        _desktop = desktop;
        _dialogs = dialogs;
        _toasts = toasts;
        _logger = logger;
        PageSize = settings.Current.FilesPageSize;
        VisibleColumns = settings.Current.FilesColumns.ToHashSet(StringComparer.Ordinal);
        _host.MediaChanged += OnMediaChanged;
        LoadLookups();
        _ = ReloadAsync();
    }

    // ---- Tree ----

    public ObservableCollection<FolderTreeNode> Folders { get; } = [];

    [ObservableProperty]
    public partial FolderTreeNode? SelectedFolder { get; set; }

    // ---- Filters ----

    [ObservableProperty]
    public partial string NameContains { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExtensionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<SelectOption> MediaOptions { get; private set; } = [];

    [ObservableProperty]
    public partial string MediaValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<SelectOption> CategoryOptions { get; private set; } = [];

    [ObservableProperty]
    public partial string CategoryValue { get; set; } = string.Empty;

    public IReadOnlyList<SelectOption> HashStatusOptions => HashOptions;

    [ObservableProperty]
    public partial string HashStatusValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MinSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxSizeText { get; set; } = string.Empty;

    public string SizeUnitLabel => _settings.Current.SizeUnit == SizeUnitSystem.Binary ? "MiB" : "MB";

    [ObservableProperty]
    public partial string ModifiedFromText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModifiedToText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    [ObservableProperty]
    public partial bool DuplicatesOnly { get; set; }

    [ObservableProperty]
    public partial bool ErrorsOnly { get; set; }

    [ObservableProperty]
    public partial string Sha1Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilterError { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> ActiveFilters { get; private set; } = [];

    // ---- Table ----

    [ObservableProperty]
    public partial IReadOnlyList<FileRow> Rows { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand), nameof(CopySha1Command), nameof(OpenContainingFolderCommand), nameof(ShowCopiesCommand))]
    public partial FileRow? SelectedRow { get; set; }

    [ObservableProperty]
    public partial string SortKey { get; private set; } = "default";

    [ObservableProperty]
    public partial bool SortDescending { get; private set; }

    public IReadOnlySet<string> VisibleColumns { get; private set; }

    // ---- Paging ----

    [ObservableProperty]
    public partial int PageIndex { get; private set; }

    [ObservableProperty]
    public partial int PageSize { get; private set; }

    public IReadOnlyList<int> PageSizes => SettingsLimits.FilePageSizes;

    [ObservableProperty]
    public partial long TotalCount { get; private set; }

    [ObservableProperty]
    public partial bool IsCounting { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial string TotalsText { get; private set; } = string.Empty;

    ICommand IFilesModel.ApplyCommand => ApplyCommand;

    ICommand IFilesModel.ClearFiltersCommand => ClearFiltersCommand;

    ICommand IFilesModel.CopyPathCommand => CopyPathCommand;

    ICommand IFilesModel.CopySha1Command => CopySha1Command;

    ICommand IFilesModel.OpenContainingFolderCommand => OpenContainingFolderCommand;

    ICommand IFilesModel.ShowCopiesCommand => ShowCopiesCommand;

    ICommand IFilesModel.ExportViewCommand => ExportViewCommand;

    ICommand IRefreshableScreen.RefreshCommand => RefreshCommand;

    public void Dispose()
    {
        _host.MediaChanged -= OnMediaChanged;
        _cancel?.Cancel();
    }

    /// <summary>Shows files matching a filter from another screen (Dashboard, Media, Categories).</summary>
    public void ApplyPreset(FileFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _suppressApply = true;
        try
        {
            ClearFields();
            SetMediaSet(filter.MediaKeys);
            MediaValue = filter.MediaKeys is not null ? MediaSetValue : filter.MediaKey?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            CategoryValue = filter.CategoryId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            ExtensionText = filter.Extension is null ? string.Empty : filter.Extension.Length == 0 ? "(none)" : filter.Extension;
            ModifiedFromText = filter.ModifiedFrom?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            ModifiedToText = filter.ModifiedTo?.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            MinSizeText = FormatSize(filter.MinSize);
            MaxSizeText = FormatSize(filter.MaxSize);
            DuplicatesOnly = filter.DuplicatesOnly;
            ErrorsOnly = filter.ErrorsOnly;
            Sha1Text = filter.Sha1 ?? string.Empty;
            NameContains = filter.NameContains ?? string.Empty;
            IncludeSubfolders = filter.IncludeSubfolders;
            HashStatusValue = filter.HashStatus?.ToString() ?? string.Empty;
            SelectedFolder = null;
        }
        finally
        {
            _suppressApply = false;
        }

        // Exact sizes (largest-file click-through) are kept exactly, not rounded through the text boxes.
        if (TryBuildFilter(out var built))
        {
            Activate(built with { MinSize = filter.MinSize, MaxSize = filter.MaxSize, FolderId = filter.FolderId });
        }
    }

    public Task SortByAsync(string key)
    {
        SortDescending = key == SortKey && !SortDescending;
        SortKey = key;
        return ReloadAsync();
    }

    public void ToggleColumn(string key)
    {
        var columns = VisibleColumns.ToHashSet(StringComparer.Ordinal);
        if (!columns.Remove(key))
        {
            columns.Add(key);
        }

        VisibleColumns = columns;
        OnPropertyChanged(nameof(VisibleColumns));
        _settings.Update(s => s.FilesColumns = [.. columns]);
    }

    public Task GoToPageAsync(int pageIndex) => LoadPageAsync(_pager, pageIndex, countTotals: false);

    public Task SetPageSizeAsync(int pageSize)
    {
        if (!SettingsLimits.FilePageSizes.Contains(pageSize) || pageSize == PageSize)
        {
            return Task.CompletedTask;
        }

        PageSize = pageSize;
        _settings.Update(s => s.FilesPageSize = pageSize);
        return ReloadAsync();
    }

    [RelayCommand]
    private void Apply()
    {
        if (TryBuildFilter(out var filter))
        {
            Activate(filter);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressApply = true;
        ClearFields();
        SelectedFolder = null;
        _suppressApply = false;
        Apply();
    }

    [RelayCommand(CanExecute = nameof(CanExportView))]
    private Task ExportView() =>
        _export!.ExportAsync(filesView: _activeFilter, filesViewText: ActiveFilters.Count == 0 ? string.Empty : string.Join(" · ", ActiveFilters));

    private bool CanExportView() => _export is not null && _host.HasSession;

    [RelayCommand]
    private Task Refresh()
    {
        LoadLookups();
        return ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedRow is { } row && _host.Config is { } config)
        {
            _desktop.SetClipboardText(ScanPaths.ToFullPath(config.RootPath, row.RelativePath));
            _toasts.Show("Path copied", ToastKind.Success);
        }
    }

    [RelayCommand(CanExecute = nameof(HasHash))]
    private void CopySha1()
    {
        if (SelectedRow?.Sha1 is { Length: > 0 } sha1)
        {
            _desktop.SetClipboardText(sha1);
            _toasts.Show("SHA-1 copied", ToastKind.Success);
        }
    }

    /// <summary>Opens Explorer with the file selected. The file itself is never opened (BRW-04).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenContainingFolder()
    {
        if (SelectedRow is not { } row || _host.Config is not { } config)
        {
            return;
        }

        var path = ScanPaths.ToFullPath(config.RootPath, row.RelativePath);
        if (File.Exists(path))
        {
            _desktop.SelectInExplorer(path);
        }
        else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
        {
            _desktop.OpenFolder(folder);
        }
        else
        {
            _toasts.Show($"'{path}' cannot be found.", ToastKind.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasHash))]
    private void ShowCopies()
    {
        if (SelectedRow?.Sha1 is { Length: > 0 } sha1)
        {
            ApplyPreset(new FileFilter { Sha1 = sha1 });
        }
    }

    private bool HasSelection() => SelectedRow is not null;

    private bool HasHash() => SelectedRow?.Sha1 is { Length: > 0 };

    partial void OnSelectedFolderChanged(FolderTreeNode? value)
    {
        if (!_suppressApply)
        {
            Apply();
        }
    }

    /// <summary>
    /// A set of media from another screen (the Dashboard's selection) is shown as an extra media option while it is in
    /// use; choosing another media, or clearing the filters, drops it.
    /// </summary>
    private void SetMediaSet(IReadOnlyCollection<long>? keys)
    {
        _mediaSet = keys is null ? null : [.. keys];
        UpdateMediaOptions();
    }

    private void UpdateMediaOptions()
    {
        MediaOptions = _mediaSet is { } set
            ? [new(string.Empty, "All media"), new(MediaSetValue, $"{set.Count:N0} media from Dashboard"), .. _mediaRootOptions]
            : [new(string.Empty, "All media"), .. _mediaRootOptions];
    }

    partial void OnMediaValueChanged(string value)
    {
        if (value != MediaSetValue && _mediaSet is not null)
        {
            SetMediaSet(null);
        }
    }

    private void ClearFields()
    {
        NameContains = ExtensionText = MinSizeText = MaxSizeText = Sha1Text = ModifiedFromText = ModifiedToText = string.Empty;
        MediaValue = CategoryValue = HashStatusValue = string.Empty;
        SetMediaSet(null);
        IncludeSubfolders = true;
        DuplicatesOnly = ErrorsOnly = false;
        FilterError = string.Empty;
    }

    private void Activate(FileFilter filter)
    {
        _activeFilter = filter;
        ActiveFilters = Describe(filter);
        _ = ReloadAsync();
    }

    private bool TryBuildFilter(out FileFilter filter)
    {
        filter = FileFilter.None;
        FilterError = string.Empty;
        var culture = CultureInfo.CurrentCulture;
        var unitBytes = _settings.Current.SizeUnit == SizeUnitSystem.Binary ? 1024d * 1024 : 1_000_000d;
        var errors = new List<string>();

        long? Size(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, culture, out var value) && value >= 0)
            {
                return (long)Math.Round(value * unitBytes);
            }

            errors.Add($"{label} must be a number of {SizeUnitLabel}.");
            return null;
        }

        DateTimeOffset? Date(string text, string label, int addDays)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return new DateTimeOffset(DateTime.SpecifyKind(date.AddDays(addDays), DateTimeKind.Utc));
            }

            errors.Add($"{label} is not a valid date.");
            return null;
        }

        var min = Size(MinSizeText, "Minimum size");
        var max = Size(MaxSizeText, "Maximum size");
        var from = Date(ModifiedFromText, "Modified from", 0);
        var to = Date(ModifiedToText, "Modified to", 1);
        var sha1 = Sha1Text.Trim();
        if (sha1.Length > 0 && (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)))
        {
            errors.Add("SHA-1 must be 40 hexadecimal characters.");
        }

        if (errors.Count > 0)
        {
            FilterError = string.Join(" ", errors);
            return false;
        }

        var extension = ExtensionText.Trim().TrimStart('.');
        filter = new FileFilter
        {
            MediaKey = long.TryParse(MediaValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaKey) ? mediaKey : null,
            MediaKeys = MediaValue == MediaSetValue ? _mediaSet : null,
            FolderId = SelectedFolder?.Info.FolderId,
            IncludeSubfolders = IncludeSubfolders,
            CategoryId = int.TryParse(CategoryValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var categoryId) ? categoryId : null,
            Extension = extension.Length == 0 && ExtensionText.Trim().Length == 0 ? null : extension is "(none)" or "" ? string.Empty : extension,
            MinSize = min,
            MaxSize = max,
            ModifiedFrom = from,
            ModifiedTo = to,
            HashStatus = Enum.TryParse<HashStatus>(HashStatusValue, out var status) ? status : null,
            DuplicatesOnly = DuplicatesOnly,
            ErrorsOnly = ErrorsOnly,
            NameContains = string.IsNullOrWhiteSpace(NameContains) ? null : NameContains.Trim(),
            Sha1 = sha1.Length == 0 ? null : sha1,
        };
        return true;
    }

    private IReadOnlyList<string> Describe(FileFilter filter)
    {
        var labels = new List<string>();
        var culture = CultureInfo.CurrentCulture;
        var unit = _settings.Current.SizeUnit;
        if (filter.MediaKey is { } key)
        {
            labels.Add("Media " + (MediaOptions.FirstOrDefault(o => o.Value == key.ToString(CultureInfo.InvariantCulture))?.Label ?? "?"));
        }

        if (filter.MediaKeys is { } keys)
        {
            labels.Add(keys.Count == 1 ? "1 media" : $"{keys.Count.ToString("N0", culture)} media");
        }

        if (filter.FolderId is not null && SelectedFolder is { } folder)
        {
            labels.Add((filter.IncludeSubfolders ? "In " : "Only in ") + folder.Info.Name);
        }

        if (filter.CategoryId is { } category)
        {
            labels.Add(CategoryOptions.FirstOrDefault(o => o.Value == category.ToString(CultureInfo.InvariantCulture))?.Label ?? "Category");
        }

        if (filter.Extension is { } extension)
        {
            labels.Add(extension.Length == 0 ? "No extension" : "." + extension);
        }

        if (filter.NameContains is { } name)
        {
            labels.Add($"Name “{name}”");
        }

        if (filter.MinSize is { } minSize)
        {
            labels.Add("≥ " + SizeFormatter.Format(minSize, unit));
        }

        if (filter.MaxSize is { } maxSize)
        {
            labels.Add("≤ " + SizeFormatter.Format(maxSize, unit));
        }

        if (filter.ModifiedFrom is { } from)
        {
            labels.Add("Modified from " + from.UtcDateTime.ToString("yyyy-MM-dd", culture));
        }

        if (filter.ModifiedTo is { } to)
        {
            labels.Add("Modified to " + to.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", culture));
        }

        if (filter.HashStatus is { } status)
        {
            labels.Add(HashOptions.First(o => o.Value == status.ToString()).Label);
        }

        if (filter.DuplicatesOnly)
        {
            labels.Add("Duplicates only");
        }

        if (filter.ErrorsOnly)
        {
            labels.Add("Errors only");
        }

        if (filter.Sha1 is { } sha1)
        {
            labels.Add("SHA-1 " + sha1[..Math.Min(10, sha1.Length)] + "…");
        }

        return labels;
    }

    private string FormatSize(long? bytes)
    {
        if (bytes is not { } value)
        {
            return string.Empty;
        }

        var unitBytes = _settings.Current.SizeUnit == SizeUnitSystem.Binary ? 1024d * 1024 : 1_000_000d;
        return (value / unitBytes).ToString("0.######", CultureInfo.CurrentCulture);
    }

    private void LoadLookups()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        _suppressApply = true;
        try
        {
            var database = session.Database;
            var roots = _queries.MediaRoots(database);
            _mediaKeys = roots.Select(r => r.MediaKey).ToHashSet();
            Folders.Clear();
            foreach (var root in roots)
            {
                Folders.Add(new FolderTreeNode(ToInfo(root), id => _queries.ChildFolders(database, id).Select(ToInfo).ToList()));
            }

            _mediaRootOptions = [.. roots.Select(r => new SelectOption(r.MediaKey.ToString(CultureInfo.InvariantCulture), r.Name))];
            UpdateMediaOptions();
            CategoryOptions =
            [
                new(string.Empty, "All categories"),
                .. _categories.Categories(database).Select(c => new SelectOption(c.CategoryId.ToString(CultureInfo.InvariantCulture), c.Name)),
            ];
            if (MediaOptions.All(o => o.Value != MediaValue))
            {
                MediaValue = string.Empty;
            }

            SelectedFolder = null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogError(ex, "Loading the Files lookups failed");
        }
        finally
        {
            _suppressApply = false;
        }
    }

    private static FolderInfo ToInfo(FolderNode node) => new(node.FolderId, node.Name, node.HasChildren, node.IsReparsePoint);

    /// <summary>New filter, sort or page size: a fresh pager, page 1, and a new count.</summary>
    private Task ReloadAsync()
    {
        if (_host.Session is not { } session)
        {
            return Task.CompletedTask;
        }

        var pager = new FilePager(_queries, session.Database, _activeFilter, ToSort(SortKey), SortDescending, PageSize);
        _pager = pager;
        return LoadPageAsync(pager, 0, countTotals: true);
    }

    private async Task LoadPageAsync(FilePager? pager, int pageIndex, bool countTotals)
    {
        if (pager is null)
        {
            return;
        }

        _cancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var token = cancel.Token;
        var unit = _settings.Current.SizeUnit;
        var zone = _settings.Current.DisplayTimeZone;

        IsLoading = true;
        if (countTotals)
        {
            _ = CountAsync(pager, unit, token);
        }

        try
        {
            var rows = await Task.Run(() => pager.Load(pageIndex, token).Select(item => ToRow(item, unit, zone)).ToList(), token);
            if (token.IsCancellationRequested || !ReferenceEquals(pager, _pager))
            {
                return;
            }

            var selectedId = SelectedRow?.FileId;
            Rows = rows;
            PageIndex = pager.PageIndex;
            SelectedRow = selectedId is { } id ? rows.FirstOrDefault(r => r.FileId == id) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogError(ex, "Loading files failed");
            FilterError = $"Files could not be loaded: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task CountAsync(FilePager pager, SizeUnitSystem unit, CancellationToken token)
    {
        IsCounting = true;
        try
        {
            await Task.Run(() => pager.CountTotals(token), token);
            if (!ReferenceEquals(pager, _pager))
            {
                return;
            }

            TotalCount = pager.TotalCount ?? 0;
            TotalsText = $"{TotalCount.ToString("N0", CultureInfo.CurrentCulture)} files · {SizeFormatter.Format(pager.TotalBytes, unit)}";
            IsCounting = false;
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogWarning(ex, "Counting files failed");
            TotalsText = string.Empty;
            IsCounting = false;
        }
    }

    internal static FileRow ToRow(FileItem item, SizeUnitSystem unit, DisplayTimeZone zone) => new(
        item.FileId,
        item.MediaId,
        item.Name,
        item.Extension,
        item.Category,
        item.FolderPath,
        item.RelativePath,
        SizeFormatter.Format(item.SizeBytes, unit),
        TimeFormatter.Format(item.CreatedUtc, zone),
        TimeFormatter.Format(item.ModifiedUtc, zone),
        TimeFormatter.Format(item.AccessedUtc, zone),
        item.HashStatus switch
        {
            HashStatus.Hashed => "Hashed",
            HashStatus.Pending => "Pending",
            HashStatus.Error => "Error",
            _ => "Skipped",
        },
        item.Sha1,
        (int)Math.Min(int.MaxValue, item.DuplicateCount));

    private static FileSortColumn ToSort(string key) => key switch
    {
        "name" => FileSortColumn.Name,
        "extension" => FileSortColumn.Extension,
        "size" => FileSortColumn.Size,
        "modified" => FileSortColumn.Modified,
        _ => FileSortColumn.Default,
    };

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        // Scans report status changes here. Keep the current page; refresh the count, and the tree if media changed.
        if (_host.Session is { } session)
        {
            var keys = _queries.MediaRoots(session.Database).Select(r => r.MediaKey).ToHashSet();
            if (!keys.SetEquals(_mediaKeys))
            {
                LoadLookups();
            }
        }

        if (_pager is { } pager && _cancel is { } cancel)
        {
            _ = CountAsync(pager, _settings.Current.SizeUnit, cancel.Token);
        }
    }
}

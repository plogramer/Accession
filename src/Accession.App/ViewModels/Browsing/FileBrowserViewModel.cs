using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.App.ViewModels.Browsing;

/// <summary>File browser (requirements 5.8, section 8.10): folder tree, filters, paged file grid.</summary>
public sealed partial class FileBrowserViewModel : ViewModelBase, IDisposable
{
    private readonly InventoryHost _host;
    private readonly FileBrowserQueries _queries;
    private readonly CategoryQueries _categories;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<FileBrowserViewModel> _logger;
    private FilePageCursor? _next;
    private FileFilter _activeFilter = FileFilter.None;
    private CancellationTokenSource? _loadCancel;
    private bool _suppressApply;

    public FileBrowserViewModel(InventoryHost host, FileBrowserQueries queries, CategoryQueries categories,
        ISettingsService settings, IDialogService dialogs, ILogger<FileBrowserViewModel> logger)
    {
        _host = host;
        _queries = queries;
        _categories = categories;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        HashStatusOptions =
        [
            new(null, "Any"),
            new(Core.Model.HashStatus.Hashed, "Hashed"),
            new(Core.Model.HashStatus.Pending, "Not hashed yet"),
            new(Core.Model.HashStatus.Error, "Could not be read"),
            new(Core.Model.HashStatus.Skipped, "Skipped (link)"),
        ];
        SelectedHashStatus = HashStatusOptions[0];
        _host.MediaChanged += OnMediaChanged;
        LoadLookups();
        _ = ReloadAsync();
    }

    public ISettingsService Settings => _settings;

    // ---- Tree ----

    public ObservableCollection<FolderTreeItem> Folders { get; } = [];

    [ObservableProperty]
    public partial FolderTreeItem? SelectedFolder { get; set; }

    // ---- Filters ----

    public ObservableCollection<Option<long?>> MediaOptions { get; } = [];

    [ObservableProperty]
    public partial Option<long?>? SelectedMedia { get; set; }

    public ObservableCollection<Option<int?>> CategoryOptions { get; } = [];

    [ObservableProperty]
    public partial Option<int?>? SelectedCategory { get; set; }

    public IReadOnlyList<Option<HashStatus?>> HashStatusOptions { get; }

    [ObservableProperty]
    public partial Option<HashStatus?> SelectedHashStatus { get; set; }

    [ObservableProperty]
    public partial string ExtensionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MinSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxSizeText { get; set; } = string.Empty;

    /// <summary>"MB" or "MiB", following the size unit setting.</summary>
    public string SizeUnitLabel => _settings.Current.SizeUnit == SizeUnitSystem.Binary ? "MiB" : "MB";

    [ObservableProperty]
    public partial DateTime? ModifiedFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? ModifiedTo { get; set; }

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    [ObservableProperty]
    public partial bool DuplicatesOnly { get; set; }

    [ObservableProperty]
    public partial bool ErrorsOnly { get; set; }

    [ObservableProperty]
    public partial string NameContains { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Sha1Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilterError { get; set; } = string.Empty;

    // ---- Grid ----

    public ObservableCollection<FileRowVm> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand), nameof(CopySha1Command), nameof(ShowDuplicatesCommand), nameof(OpenContainingFolderCommand))]
    public partial FileRowVm? SelectedRow { get; set; }

    [ObservableProperty]
    public partial FileSortColumn SortColumn { get; set; }

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string TotalsText { get; set; } = string.Empty;

    public void Dispose()
    {
        _host.MediaChanged -= OnMediaChanged;
        _loadCancel?.Cancel();
    }

    /// <summary>Applies a filter coming from another screen (dashboard, categories).</summary>
    public void ApplyPreset(FileFilter filter)
    {
        _suppressApply = true;
        try
        {
            ClearFields();
            SelectedMedia = MediaOptions.FirstOrDefault(o => o.Value == filter.MediaKey) ?? MediaOptions.FirstOrDefault();
            SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == filter.CategoryId) ?? CategoryOptions.FirstOrDefault();
            ExtensionText = filter.Extension is null ? string.Empty : filter.Extension.Length == 0 ? "(none)" : filter.Extension;
            ModifiedFrom = filter.ModifiedFrom?.UtcDateTime;
            ModifiedTo = filter.ModifiedTo?.UtcDateTime.AddDays(-1);
            DuplicatesOnly = filter.DuplicatesOnly;
            ErrorsOnly = filter.ErrorsOnly;
            Sha1Text = filter.Sha1 ?? string.Empty;
            NameContains = filter.NameContains ?? string.Empty;
            IncludeSubfolders = filter.IncludeSubfolders;
            SelectedHashStatus = HashStatusOptions.FirstOrDefault(o => o.Value == filter.HashStatus) ?? HashStatusOptions[0];
            if (filter.FolderId is { } folderId)
            {
                SelectFolder(folderId);
            }
        }
        finally
        {
            _suppressApply = false;
        }

        _ = ApplyAsync(folderOverride: filter.FolderId);
    }

    /// <summary>Called by the view when a column header is clicked.</summary>
    public void SortBy(FileSortColumn column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private Task Apply() => ApplyAsync(folderOverride: null);

    [RelayCommand]
    private Task ClearFilters()
    {
        _suppressApply = true;
        ClearFields();
        if (SelectedFolder is not null)
        {
            SelectedFolder.IsSelected = false;
            SelectedFolder = null;
        }

        _suppressApply = false;
        return ApplyAsync(folderOverride: null);
    }

    [RelayCommand]
    private Task Refresh()
    {
        LoadLookups();
        return ReloadAsync();
    }

    [RelayCommand]
    private Task LoadMore() => LoadPageAsync(append: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedRow is { } row && _host.Config is { } config)
        {
            Clipboard.SetText(ScanPaths.ToFullPath(config.RootPath, row.RelativePath));
        }
    }

    [RelayCommand(CanExecute = nameof(HasHash))]
    private void CopySha1()
    {
        if (SelectedRow?.Sha1 is { Length: > 0 } sha1)
        {
            Clipboard.SetText(sha1);
        }
    }

    [RelayCommand(CanExecute = nameof(HasHash))]
    private void ShowDuplicates()
    {
        if (SelectedRow?.Sha1 is { Length: > 0 } sha1)
        {
            ApplyPreset(new FileFilter { Sha1 = sha1 });
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
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
        }
        else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
        }
        else
        {
            _dialogs.ShowWarning("Open containing folder", $"'{path}' cannot be found.");
        }
    }

    private bool HasSelection() => SelectedRow is not null;

    private bool HasHash() => SelectedRow?.Sha1 is { Length: > 0 };

    partial void OnSelectedFolderChanged(FolderTreeItem? value)
    {
        if (!_suppressApply)
        {
            _ = ApplyAsync(folderOverride: null);
        }
    }

    private void ClearFields()
    {
        SelectedMedia = MediaOptions.FirstOrDefault();
        SelectedCategory = CategoryOptions.FirstOrDefault();
        SelectedHashStatus = HashStatusOptions[0];
        ExtensionText = MinSizeText = MaxSizeText = NameContains = Sha1Text = string.Empty;
        ModifiedFrom = ModifiedTo = null;
        IncludeSubfolders = true;
        DuplicatesOnly = ErrorsOnly = false;
        FilterError = string.Empty;
    }

    private void SelectFolder(long folderId)
    {
        // Only folders already loaded in the tree can be selected; the filter applies regardless.
        FolderTreeItem? Find(IEnumerable<FolderTreeItem> items) =>
            items.Select(i => i.Node.FolderId == folderId ? i : Find(i.Children)).FirstOrDefault(i => i is not null);
        if (Find(Folders) is { } item)
        {
            item.IsSelected = true;
            SelectedFolder = item;
        }
    }

    private Task ApplyAsync(long? folderOverride)
    {
        if (!TryBuildFilter(folderOverride, out var filter))
        {
            return Task.CompletedTask;
        }

        _activeFilter = filter;
        return ReloadAsync();
    }

    private bool TryBuildFilter(long? folderOverride, out FileFilter filter)
    {
        filter = FileFilter.None;
        FilterError = string.Empty;
        var unitBytes = _settings.Current.SizeUnit == SizeUnitSystem.Binary ? 1024d * 1024 : 1_000_000d;
        long? ParseSize(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value >= 0)
            {
                return (long)Math.Round(value * unitBytes);
            }

            FilterError = $"{label} must be a number of {SizeUnitLabel}.";
            return null;
        }

        var min = ParseSize(MinSizeText, "Minimum size");
        var max = ParseSize(MaxSizeText, "Maximum size");
        if (FilterError.Length > 0)
        {
            return false;
        }

        var extension = ExtensionText.Trim();
        filter = new FileFilter
        {
            MediaKey = SelectedMedia?.Value,
            FolderId = folderOverride ?? SelectedFolder?.Node.FolderId,
            IncludeSubfolders = IncludeSubfolders,
            CategoryId = SelectedCategory?.Value,
            Extension = extension.Length == 0 ? null : extension is "(none)" or "." ? string.Empty : extension,
            MinSize = min,
            MaxSize = max,
            ModifiedFrom = ModifiedFrom is { } from ? new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Utc)) : null,
            ModifiedTo = ModifiedTo is { } to ? new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc)) : null,
            HashStatus = SelectedHashStatus?.Value,
            DuplicatesOnly = DuplicatesOnly,
            ErrorsOnly = ErrorsOnly,
            NameContains = string.IsNullOrWhiteSpace(NameContains) ? null : NameContains,
            Sha1 = string.IsNullOrWhiteSpace(Sha1Text) ? null : Sha1Text,
        };
        return true;
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
            var mediaKey = SelectedMedia?.Value;
            var categoryId = SelectedCategory?.Value;
            var roots = _queries.MediaRoots(session.Database);

            Folders.Clear();
            foreach (var root in roots)
            {
                Folders.Add(new FolderTreeItem(root, id => _queries.ChildFolders(session.Database, id)));
            }

            MediaOptions.Clear();
            MediaOptions.Add(new Option<long?>(null, "All media"));
            foreach (var root in roots)
            {
                MediaOptions.Add(new Option<long?>(root.MediaKey, root.Name));
            }

            CategoryOptions.Clear();
            CategoryOptions.Add(new Option<int?>(null, "All categories"));
            foreach (var category in _categories.Categories(session.Database))
            {
                CategoryOptions.Add(new Option<int?>(category.CategoryId, category.Name));
            }

            SelectedMedia = MediaOptions.FirstOrDefault(o => o.Value == mediaKey) ?? MediaOptions[0];
            SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == categoryId) ?? CategoryOptions[0];
            SelectedFolder = null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogError(ex, "Loading the File browser lookups failed");
        }
        finally
        {
            _suppressApply = false;
        }
    }

    private async Task ReloadAsync()
    {
        await LoadPageAsync(append: false);
    }

    private async Task LoadPageAsync(bool append)
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        if (!append)
        {
            _loadCancel?.Cancel();
            _loadCancel = new CancellationTokenSource();
            _next = null;
            Rows.Clear();
        }
        else if (_next is null || IsLoading)
        {
            return;
        }

        var token = _loadCancel!.Token;
        var filter = _activeFilter;
        var sort = SortColumn;
        var descending = SortDescending;
        var cursor = _next;
        var unit = _settings.Current.SizeUnit;
        var zone = _settings.Current.DisplayTimeZone;
        IsLoading = true;
        try
        {
            var page = await Task.Run(() => _queries.Page(session.Database, filter, sort, descending, cursor, cancellationToken: token), token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var item in page.Items)
            {
                Rows.Add(new FileRowVm(item, unit, zone));
            }

            _next = page.Next;
            HasMore = _next is not null;
            if (!append)
            {
                _ = LoadTotalsAsync(session, filter, unit, token);
            }
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

    private async Task LoadTotalsAsync(Accession.Data.Sessions.InventorySession session, FileFilter filter, SizeUnitSystem unit, CancellationToken token)
    {
        TotalsText = "counting…";
        try
        {
            var totals = await Task.Run(() => _queries.Totals(session.Database, filter, token), token);
            if (!token.IsCancellationRequested)
            {
                TotalsText = $"{totals.FileCount:N0} files · {SizeFormatter.Format(totals.TotalBytes, unit)}";
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogWarning(ex, "Counting files failed");
            TotalsText = string.Empty;
        }
    }

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        LoadLookups();
        _ = ReloadAsync();
    }
}

/// <summary>A file row with display-ready values.</summary>
public sealed class FileRowVm(FileItem item, SizeUnitSystem unit, DisplayTimeZone zone)
{
    public FileItem Item { get; } = item;

    public string MediaId => Item.MediaId;

    public string Name => Item.Name;

    public string Extension => Item.Extension;

    public string Category => Item.Category;

    public string RelativePath => Item.RelativePath;

    public string FolderPath => Item.FolderPath;

    public string Size { get; } = SizeFormatter.Format(item.SizeBytes, unit);

    public long SizeBytes => Item.SizeBytes;

    public string Created { get; } = TimeFormatter.Format(item.CreatedUtc, zone);

    public string Modified { get; } = TimeFormatter.Format(item.ModifiedUtc, zone);

    public string Accessed { get; } = TimeFormatter.Format(item.AccessedUtc, zone);

    public string? Sha1 => Item.Sha1;

    public string HashStatus { get; } = item.HashStatus switch
    {
        Core.Model.HashStatus.Hashed => "Hashed",
        Core.Model.HashStatus.Pending => "Pending",
        Core.Model.HashStatus.Error => "Error",
        _ => "Skipped",
    };

    public string Duplicates { get; } = item.DuplicateCount > 1 ? (item.DuplicateCount - 1).ToString("N0", CultureInfo.CurrentCulture) : string.Empty;
}

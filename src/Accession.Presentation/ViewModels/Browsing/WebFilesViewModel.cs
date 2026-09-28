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
using Accession.Data.SavedSearches;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
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
    private readonly SavedSearchWorkflow? _savedSearchWorkflow;
    private readonly CopyWorkflow? _copyWorkflow;
    private readonly HashSet<long> _checked = [];
    // The "where" of the view is one of: the tree's SelectedFolder (a media root or a folder), SelectedSavedSearch,
    // a set of media handed over by the Dashboard, or a media without folders yet (not in the tree).
    private IReadOnlyList<long>? _mediaSet;
    private long? _mediaWithoutFolders;
    private Dictionary<long, FolderTreeNode> _mediaRoots = [];
    private Dictionary<long, string> _mediaNames = [];

    public WebFilesViewModel(InventoryHost host, FileBrowserQueries queries, CategoryQueries categories, ISettingsService settings,
        IDesktop desktop, IDialogService dialogs, ToastService toasts, ILogger<WebFilesViewModel> logger, ExportWorkflow? export = null,
        SavedSearchWorkflow? savedSearches = null, CopyWorkflow? copy = null)
    {
        _export = export;
        _copyWorkflow = copy;
        _savedSearchWorkflow = savedSearches;
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

    // ---- Saved searches ----

    [ObservableProperty]
    public partial string SideTab { get; set; } = "folders";

    public ObservableCollection<SavedSearchRow> SavedSearches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCheckedFromSavedSearchCommand), nameof(RemoveAllFromSavedSearchCommand))]
    public partial SavedSearchRow? SelectedSavedSearch { get; set; }

    public bool CanChangeSavedSearches => _savedSearchWorkflow?.CanChange ?? false;

    ICommand IFilesModel.NewSavedSearchCommand => NewSavedSearchCommand;

    ICommand IFilesModel.EditSavedSearchCommand => EditSavedSearchCommand;

    ICommand IFilesModel.DeleteSavedSearchCommand => DeleteSavedSearchCommand;

    ICommand IFilesModel.AddAllToSavedSearchCommand => AddAllToSavedSearchCommand;

    ICommand IFilesModel.AddCheckedToSavedSearchCommand => AddCheckedToSavedSearchCommand;

    ICommand IFilesModel.RemoveCheckedFromSavedSearchCommand => RemoveCheckedFromSavedSearchCommand;

    ICommand IFilesModel.RemoveAllFromSavedSearchCommand => RemoveAllFromSavedSearchCommand;

    // ---- Ticked rows ----

    public IReadOnlySet<long> CheckedFileIds => _checked;

    ICommand IFilesModel.ClearCheckedCommand => ClearCheckedCommand;

    public void SetChecked(FileRow row, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (isChecked ? _checked.Add(row.FileId) : _checked.Remove(row.FileId))
        {
            OnCheckedChanged();
        }
    }

    public void SetPageChecked(bool isChecked)
    {
        foreach (var row in Rows)
        {
            if (isChecked)
            {
                _checked.Add(row.FileId);
            }
            else
            {
                _checked.Remove(row.FileId);
            }
        }

        OnCheckedChanged();
    }

    [RelayCommand]
    private void ClearChecked()
    {
        _checked.Clear();
        OnCheckedChanged();
    }

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedFileIds));
        AddCheckedToSavedSearchCommand.NotifyCanExecuteChanged();
        RemoveCheckedFromSavedSearchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChangeSavedSearches))]
    private void NewSavedSearch()
    {
        if (_savedSearchWorkflow!.Create() is { } id)
        {
            LoadSavedSearches();
            SelectSavedSearch(id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSavedSearches))]
    private void EditSavedSearch(SavedSearchRow? row)
    {
        if (row is not null && Info(row) is { } info && _savedSearchWorkflow!.Edit(info))
        {
            LoadSavedSearches();
            if (SelectedSavedSearch is not null)
            {
                ActiveFilters = Describe(_activeFilter);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSavedSearches))]
    private void DeleteSavedSearch(SavedSearchRow? row)
    {
        if (row is not null && Info(row) is { } info && _savedSearchWorkflow!.Delete(info))
        {
            var wasShown = SelectedSavedSearch?.Id == row.Id;
            LoadSavedSearches(); // drops the selection if it was the deleted one
            if (wasShown)
            {
                Apply(); // back to all media
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddAll))]
    private async Task AddAllToSavedSearch(SavedSearchRow? target)
    {
        if (await _savedSearchWorkflow!.AddAsync(target?.Id, target?.Name, _activeFilter) is not null)
        {
            LoadSavedSearches(); // new counts; the results stay in view
        }
    }

    private bool CanAddAll(SavedSearchRow? target) => CanChangeSavedSearches && TotalCount > 0 && target?.Id != SelectedSavedSearch?.Id;

    [RelayCommand(CanExecute = nameof(CanAddChecked))]
    private async Task AddCheckedToSavedSearch(SavedSearchRow? target)
    {
        if (await _savedSearchWorkflow!.AddAsync(target?.Id, target?.Name, new FileFilter { FileIds = [.. _checked] }) is not null)
        {
            ClearChecked();
            LoadSavedSearches();
        }
    }

    private bool CanAddChecked(SavedSearchRow? target) => CanChangeSavedSearches && _checked.Count > 0 && (target is null || target.Id != SelectedSavedSearch?.Id);

    [RelayCommand(CanExecute = nameof(CanRemoveChecked))]
    private async Task RemoveCheckedFromSavedSearch()
    {
        if (SelectedSavedSearch is { } shown
            && await _savedSearchWorkflow!.RemoveAsync(shown.Id, shown.Name, new FileFilter { FileIds = [.. _checked] }, _checked.Count))
        {
            ClearChecked();
            LoadSavedSearches();
            await ReloadAsync();
        }
    }

    private bool CanRemoveChecked() => CanChangeSavedSearches && SelectedSavedSearch is not null && _checked.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRemoveAll))]
    private async Task RemoveAllFromSavedSearch()
    {
        if (SelectedSavedSearch is { } shown && await _savedSearchWorkflow!.RemoveAsync(shown.Id, shown.Name, _activeFilter, TotalCount))
        {
            ClearChecked();
            LoadSavedSearches();
            await ReloadAsync();
        }
    }

    private bool CanRemoveAll() => CanChangeSavedSearches && SelectedSavedSearch is not null && TotalCount > 0;

    partial void OnTotalCountChanged(long value)
    {
        AddAllToSavedSearchCommand.NotifyCanExecuteChanged();
        RemoveAllFromSavedSearchCommand.NotifyCanExecuteChanged();
    }

    private SavedSearchInfo? Info(SavedSearchRow row) =>
        _savedSearchWorkflow?.List().FirstOrDefault(s => s.SavedSearchId == row.Id);

    private void LoadSavedSearches()
    {
        var unit = _settings.Current.SizeUnit;
        var zone = _settings.Current.DisplayTimeZone;
        var culture = CultureInfo.CurrentCulture;
        var selectedId = SelectedSavedSearch?.Id;
        SavedSearches.Clear();
        foreach (var s in _savedSearchWorkflow?.List() ?? [])
        {
            SavedSearches.Add(new SavedSearchRow(s.SavedSearchId, s.Name, s.Description ?? string.Empty, s.FileCount,
                s.FileCount == 1 ? "1 file" : $"{s.FileCount.ToString("N0", culture)} files", SizeFormatter.Format(s.TotalBytes, unit),
                SavedSearchViewModel.Describe("Created", s.CreatedBy, s.CreatedOnMachine, s.CreatedAtUtc, zone)));
        }

        // Keep the shown saved search selected (the row objects are new) without reloading the files.
        _suppressApply = true;
        SelectedSavedSearch = SavedSearches.FirstOrDefault(s => s.Id == selectedId);
        _suppressApply = false;
        OnPropertyChanged(nameof(CanChangeSavedSearches));
        NewSavedSearchCommand.NotifyCanExecuteChanged();
    }

    private void SelectSavedSearch(long id)
    {
        SideTab = "saved";
        SelectedSavedSearch = SavedSearches.FirstOrDefault(s => s.Id == id);
    }

    partial void OnSelectedSavedSearchChanged(SavedSearchRow? value)
    {
        if (_suppressApply)
        {
            return;
        }

        if (value is not null)
        {
            // A saved search replaces the folder or media set being shown.
            _suppressApply = true;
            SelectedFolder = null;
            _mediaSet = null;
            _mediaWithoutFolders = null;
            _suppressApply = false;
        }

        Apply();
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
    public partial IReadOnlyList<FilterChip> ActiveFilters { get; private set; } = [];

    ICommand IFilesModel.RemoveFilterCommand => RemoveFilterCommand;

    /// <summary>All media: leaves the folder, saved search or media set being shown (the other filters stay).</summary>
    public void ShowAllMedia()
    {
        _suppressApply = true;
        ClearWhere();
        _suppressApply = false;
        Apply();
    }

    /// <summary>Removes one filter (the × on its chip). "where" removes the media, folder or saved search.</summary>
    [RelayCommand]
    private void RemoveFilter(string? key)
    {
        _suppressApply = true;
        switch (key)
        {
            case FilterKeys.Where: ClearWhere(); break;
            case FilterKeys.Category: CategoryValue = string.Empty; break;
            case FilterKeys.Extension: ExtensionText = string.Empty; break;
            case FilterKeys.Name: NameContains = string.Empty; break;
            case FilterKeys.MinSize: MinSizeText = string.Empty; break;
            case FilterKeys.MaxSize: MaxSizeText = string.Empty; break;
            case FilterKeys.ModifiedFrom: ModifiedFromText = string.Empty; break;
            case FilterKeys.ModifiedTo: ModifiedToText = string.Empty; break;
            case FilterKeys.Hash: HashStatusValue = string.Empty; break;
            case FilterKeys.Duplicates: DuplicatesOnly = false; break;
            case FilterKeys.Errors: ErrorsOnly = false; break;
            case FilterKeys.Sha1: Sha1Text = string.Empty; break;
        }

        _suppressApply = false;
        Apply();
    }

    private void ClearWhere()
    {
        SelectedFolder = null;
        SelectedSavedSearch = null;
        _mediaSet = null;
        _mediaWithoutFolders = null;
    }

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

    public bool CanCopyFiles => _copyWorkflow is not null && _host.HasSession;

    ICommand IFilesModel.GenerateCopyBatchCommand => GenerateCopyBatchCommand;

    ICommand IFilesModel.CopyFilesCommand => CopyFilesCommand;

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
            ClearWhere();
            if (filter.MediaKeys is { } keys)
            {
                _mediaSet = [.. keys];
            }
            else if (filter.MediaKey is { } key)
            {
                // "Browse files" for a media: select it in the Media tree, so it is visible and the next tree click replaces it.
                if (_mediaRoots.TryGetValue(key, out var root))
                {
                    SideTab = "folders";
                    SelectedFolder = root;
                    root.IsExpanded = true;
                }
                else
                {
                    _mediaWithoutFolders = key;
                }
            }

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
        }
        finally
        {
            _suppressApply = false;
        }

        // Exact sizes (largest-file click-through) are kept exactly, not rounded through the text boxes.
        if (TryBuildFilter(out var built))
        {
            Activate(built with { MinSize = filter.MinSize, MaxSize = filter.MaxSize });
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
        ClearWhere();
        _suppressApply = false;
        Apply();
    }

    [RelayCommand(CanExecute = nameof(CanExportView))]
    private Task ExportView() =>
        _export!.ExportAsync(filesView: _activeFilter, filesViewText: ViewText());

    private bool CanExportView() => _export is not null && _host.HasSession;

    /// <summary>Writes a .bat that copies the ticked rows or all results (requirements 5.8b).</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFiles))]
    private Task GenerateCopyBatch() => _copyWorkflow!.GenerateBatchAsync([.. _checked], _activeFilter, ViewText());

    /// <summary>Copies the ticked rows or all results in the app (requirements 5.8b).</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFiles))]
    private Task CopyFiles() => _copyWorkflow!.CopyFilesAsync([.. _checked], _activeFilter, ViewText());

    // ---- Right-click menus ----

    private IReadOnlyList<long> _contextTargets = [];
    private FolderTreeNode? _contextFolder;

    /// <summary>What the file menu acts on: the right-clicked file's name, or "3 ticked files".</summary>
    [ObservableProperty]
    public partial string ContextHeader { get; private set; } = string.Empty;

    /// <summary>The folder (or media) the tree's menu acts on.</summary>
    [ObservableProperty]
    public partial string ContextFolderName { get; private set; } = string.Empty;

    /// <summary>Right-click on a file: a ticked row acts on all ticked rows; any other row is selected and acts alone.</summary>
    public void OpenFileMenu(FileRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (SelectedRow?.FileId != row.FileId)
        {
            SelectedRow = row;
        }

        _contextTargets = _checked.Contains(row.FileId) && _checked.Count > 1 ? [.. _checked] : [row.FileId];
        ContextHeader = _contextTargets.Count == 1 ? row.Name : $"{_contextTargets.Count.ToString("N0", CultureInfo.CurrentCulture)} ticked files";
        AddTargetsToSavedSearchCommand.NotifyCanExecuteChanged();
        CopyToCommand.NotifyCanExecuteChanged();
    }

    public void OpenFolderMenu(FolderTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _contextFolder = node;
        ContextFolderName = node.Info.Name;
    }

    [RelayCommand]
    private Task CopyTargetSha1s() => CopyTargetsAsync(f => f.Sha1, "SHA-1", "No SHA-1 yet: the file is not hashed.");

    [RelayCommand]
    private Task CopyTargetPaths() => CopyTargetsAsync(f => _host.Config is { } c ? ScanPaths.ToFullPath(c.RootPath, f.RelativePath) : null, "Path", null);

    [RelayCommand]
    private Task CopyTargetNames() => CopyTargetsAsync(f => f.Name, "File name", null);

    /// <summary>Copies one value per target file (one per line), read from the inventory in folder, name order.</summary>
    private async Task CopyTargetsAsync(Func<FileItem, string?> value, string what, string? noneText)
    {
        if (_host.Session is not { } session || _contextTargets.Count == 0)
        {
            return;
        }

        var ids = _contextTargets;
        try
        {
            var lines = await Task.Run(() =>
            {
                using var scope = session.Database.Open();
                return _queries.StreamForExport(scope, new FileFilter { FileIds = ids }).Select(value).OfType<string>().Where(v => v.Length > 0).ToList();
            });
            if (lines.Count == 0)
            {
                _toasts.Show(noneText ?? "Nothing to copy.", ToastKind.Warning);
                return;
            }

            _desktop.SetClipboardText(string.Join(Environment.NewLine, lines));
            _toasts.Show(lines.Count == 1 ? $"{what} copied" : $"{lines.Count.ToString("N0", CultureInfo.CurrentCulture)} values copied ({what}, one per line)",
                ToastKind.Success);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogError(ex, "Copying {What} failed", what);
            _toasts.Show($"{what} could not be copied: {ex.Message}", ToastKind.Error);
        }
    }

    /// <summary>Copy To: the target files into one folder as &lt;sha1&gt;_&lt;name&gt;.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyTo))]
    private Task CopyTo() => _copyWorkflow!.CopyToAsync(_contextTargets);

    private bool CanCopyTo() => CanCopyFiles && _contextTargets.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddTargets))]
    private async Task AddTargetsToSavedSearch(SavedSearchRow? target)
    {
        if (await _savedSearchWorkflow!.AddAsync(target?.Id, target?.Name, new FileFilter { FileIds = [.. _contextTargets] }) is not null)
        {
            LoadSavedSearches();
        }
    }

    private bool CanAddTargets(SavedSearchRow? target) =>
        CanChangeSavedSearches && _contextTargets.Count > 0 && (target is null || target.Id != SelectedSavedSearch?.Id);

    /// <summary>Filters the list to the selected file's extension.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FilterByExtension()
    {
        if (SelectedRow is { } row)
        {
            ExtensionText = row.Extension.Length == 0 ? "(none)" : row.Extension;
            Apply();
        }
    }

    [RelayCommand]
    private void OpenFolderInExplorer()
    {
        if (FolderFullPath() is not { } path)
        {
            return;
        }

        if (Directory.Exists(path))
        {
            _desktop.OpenFolder(path);
        }
        else
        {
            _toasts.Show($"'{path}' cannot be found.", ToastKind.Warning);
        }
    }

    [RelayCommand]
    private void CopyFolderPath()
    {
        if (FolderFullPath() is { } path)
        {
            _desktop.SetClipboardText(path);
            _toasts.Show("Folder path copied", ToastKind.Success);
        }
    }

    /// <summary>The Copy files dialog for everything in the folder and its subfolders (other filters do not apply).</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFiles))]
    private Task CopyFolder() =>
        _contextFolder is { } node ? _copyWorkflow!.CopyFilesAsync([], FolderFilter(node), FolderText(node)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanExportView))]
    private Task ExportFolder() =>
        _contextFolder is { } node ? _export!.ExportAsync(filesView: FolderFilter(node), filesViewText: FolderText(node)) : Task.CompletedTask;

    private static FileFilter FolderFilter(FolderTreeNode node) => new() { FolderId = node.Info.FolderId, IncludeSubfolders = true };

    private static string FolderText(FolderTreeNode node) =>
        node.Depth == 0 ? $"Media {node.Info.Name}" : $"Folder {node.Info.Name} and its subfolders";

    private string? FolderFullPath()
    {
        if (_contextFolder is not { } node || _host.Session is not { } session || _host.Config is not { } config)
        {
            return null;
        }

        try
        {
            return _queries.FolderPath(session.Database, node.Info.FolderId) is { } relative ? ScanPaths.ToFullPath(config.RootPath, relative) : null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            _logger.LogError(ex, "Reading the folder path failed");
            return null;
        }
    }

    ICommand IFilesModel.CopyTargetSha1sCommand => CopyTargetSha1sCommand;

    ICommand IFilesModel.CopyTargetPathsCommand => CopyTargetPathsCommand;

    ICommand IFilesModel.CopyTargetNamesCommand => CopyTargetNamesCommand;

    ICommand IFilesModel.CopyToCommand => CopyToCommand;

    ICommand IFilesModel.AddTargetsToSavedSearchCommand => AddTargetsToSavedSearchCommand;

    ICommand IFilesModel.FilterByExtensionCommand => FilterByExtensionCommand;

    ICommand IFilesModel.OpenFolderInExplorerCommand => OpenFolderInExplorerCommand;

    ICommand IFilesModel.CopyFolderPathCommand => CopyFolderPathCommand;

    ICommand IFilesModel.CopyFolderCommand => CopyFolderCommand;

    ICommand IFilesModel.ExportFolderCommand => ExportFolderCommand;

    private string ViewText() => ActiveFilters.Count == 0 ? string.Empty : string.Join(" · ", ActiveFilters.Select(c => c.Label));

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
        if (_suppressApply)
        {
            return;
        }

        if (value is not null)
        {
            // A media or folder in the tree replaces the saved search or media set being shown; the other filters stay.
            _suppressApply = true;
            SelectedSavedSearch = null;
            _mediaSet = null;
            _mediaWithoutFolders = null;
            _suppressApply = false;
        }

        Apply();
    }

    private void ClearFields()
    {
        NameContains = ExtensionText = MinSizeText = MaxSizeText = Sha1Text = ModifiedFromText = ModifiedToText = string.Empty;
        CategoryValue = HashStatusValue = string.Empty;
        IncludeSubfolders = true;
        DuplicatesOnly = ErrorsOnly = false;
        FilterError = string.Empty;
    }

    private void Activate(FileFilter filter)
    {
        if (_checked.Count > 0)
        {
            _checked.Clear(); // ticks belong to the previous results
            OnCheckedChanged();
        }

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
            MediaKey = _mediaWithoutFolders,
            MediaKeys = _mediaSet,
            SavedSearchId = SelectedSavedSearch?.Id,
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

    private IReadOnlyList<FilterChip> Describe(FileFilter filter)
    {
        var chips = new List<FilterChip>();
        var culture = CultureInfo.CurrentCulture;
        var unit = _settings.Current.SizeUnit;
        void Add(string key, string label) => chips.Add(new FilterChip(key, label));

        if (filter.SavedSearchId is { } savedSearchId)
        {
            Add(FilterKeys.Where, "Saved search: " + (SavedSearches.FirstOrDefault(s => s.Id == savedSearchId)?.Name ?? "?"));
        }

        if (filter.MediaKey is { } key)
        {
            Add(FilterKeys.Where, "Media " + _mediaNames.GetValueOrDefault(key, "?"));
        }

        if (filter.MediaKeys is { } keys)
        {
            Add(FilterKeys.Where, keys.Count == 1 ? "1 media from Dashboard" : $"{keys.Count.ToString("N0", culture)} media from Dashboard");
        }

        if (filter.FolderId is not null && SelectedFolder is { } folder)
        {
            Add(FilterKeys.Where, folder.Depth == 0 && filter.IncludeSubfolders
                ? "Media " + folder.Info.Name
                : (filter.IncludeSubfolders ? "In " : "Only in ") + folder.Info.Name);
        }

        if (filter.CategoryId is { } category)
        {
            Add(FilterKeys.Category, CategoryOptions.FirstOrDefault(o => o.Value == category.ToString(CultureInfo.InvariantCulture))?.Label ?? "Category");
        }

        if (filter.Extension is { } extension)
        {
            Add(FilterKeys.Extension, extension.Length == 0 ? "No extension" : "." + extension);
        }

        if (filter.NameContains is { } name)
        {
            Add(FilterKeys.Name, $"Name “{name}”");
        }

        if (filter.MinSize is { } minSize)
        {
            Add(FilterKeys.MinSize, "≥ " + SizeFormatter.Format(minSize, unit));
        }

        if (filter.MaxSize is { } maxSize)
        {
            Add(FilterKeys.MaxSize, "≤ " + SizeFormatter.Format(maxSize, unit));
        }

        if (filter.ModifiedFrom is { } from)
        {
            Add(FilterKeys.ModifiedFrom, "Modified from " + from.UtcDateTime.ToString("yyyy-MM-dd", culture));
        }

        if (filter.ModifiedTo is { } to)
        {
            Add(FilterKeys.ModifiedTo, "Modified to " + to.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", culture));
        }

        if (filter.HashStatus is { } status)
        {
            Add(FilterKeys.Hash, HashOptions.First(o => o.Value == status.ToString()).Label);
        }

        if (filter.DuplicatesOnly)
        {
            Add(FilterKeys.Duplicates, "Duplicates only");
        }

        if (filter.ErrorsOnly)
        {
            Add(FilterKeys.Errors, "Errors only");
        }

        if (filter.Sha1 is { } sha1)
        {
            Add(FilterKeys.Sha1, "SHA-1 " + sha1[..Math.Min(10, sha1.Length)] + "…");
        }

        return chips;
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
            _mediaRoots = [];
            foreach (var root in roots)
            {
                var node = new FolderTreeNode(ToInfo(root), id => _queries.ChildFolders(database, id).Select(ToInfo).ToList());
                Folders.Add(node);
                _mediaRoots[root.MediaKey] = node;
            }

            using (var scope = database.Open())
            {
                _mediaNames = new Accession.Data.Repositories.MediaRepository(scope).ListActive().ToDictionary(m => m.MediaKey, m => m.MediaId);
            }

            CategoryOptions =
            [
                new(string.Empty, "All categories"),
                .. _categories.Categories(database).Select(c => new SelectOption(c.CategoryId.ToString(CultureInfo.InvariantCulture), c.Name)),
            ];
            SelectedFolder = null;
            LoadSavedSearches();
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

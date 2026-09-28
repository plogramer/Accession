using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Shell;

namespace Accession.UI.FilesScreen;

/// <summary>The Files screen (requirements 5.8, section 8.10): folder tree, filters and a paged file table.</summary>
public interface IFilesModel : INotifyPropertyChanged, IRefreshableScreen
{
    // ---- Tree ----
    ObservableCollection<FolderTreeNode> Folders { get; }
    FolderTreeNode? SelectedFolder { get; set; }

    // ---- Saved searches (the side panel's second tab) ----

    /// <summary>"folders" or "saved".</summary>
    string SideTab { get; set; }

    ObservableCollection<SavedSearchRow> SavedSearches { get; }

    /// <summary>The saved search whose files are shown; setting it leaves the folder tree, and vice versa.</summary>
    SavedSearchRow? SelectedSavedSearch { get; set; }

    /// <summary>False on a read-only inventory.</summary>
    bool CanChangeSavedSearches { get; }

    ICommand NewSavedSearchCommand { get; }

    /// <summary>Parameter: the <see cref="SavedSearchRow"/>.</summary>
    ICommand EditSavedSearchCommand { get; }

    /// <summary>Parameter: the <see cref="SavedSearchRow"/>.</summary>
    ICommand DeleteSavedSearchCommand { get; }

    /// <summary>Adds every file matching the filters. Parameter: the target <see cref="SavedSearchRow"/>, or null for a new one.</summary>
    ICommand AddAllToSavedSearchCommand { get; }

    /// <summary>Adds the ticked rows. Parameter: the target <see cref="SavedSearchRow"/>, or null for a new one.</summary>
    ICommand AddCheckedToSavedSearchCommand { get; }

    /// <summary>Removes the ticked rows from the saved search being shown.</summary>
    ICommand RemoveCheckedFromSavedSearchCommand { get; }

    /// <summary>Removes every file matching the filters from the saved search being shown.</summary>
    ICommand RemoveAllFromSavedSearchCommand { get; }

    // ---- Ticked rows (kept across pages until the filters change) ----
    IReadOnlySet<long> CheckedFileIds { get; }

    void SetChecked(FileRow row, bool isChecked);

    /// <summary>Ticks or unticks every row on the current page.</summary>
    void SetPageChecked(bool isChecked);

    ICommand ClearCheckedCommand { get; }

    // ---- Filters (applied with ApplyCommand) ----
    string NameContains { get; set; }
    string ExtensionText { get; set; }
    IReadOnlyList<SelectOption> CategoryOptions { get; }
    string CategoryValue { get; set; }
    IReadOnlyList<SelectOption> HashStatusOptions { get; }
    string HashStatusValue { get; set; }
    string MinSizeText { get; set; }
    string MaxSizeText { get; set; }
    string SizeUnitLabel { get; }

    /// <summary>yyyy-MM-dd (HTML date input) or empty.</summary>
    string ModifiedFromText { get; set; }

    /// <summary>yyyy-MM-dd (HTML date input), inclusive, or empty.</summary>
    string ModifiedToText { get; set; }

    bool IncludeSubfolders { get; set; }
    bool DuplicatesOnly { get; set; }
    bool ErrorsOnly { get; set; }
    string Sha1Text { get; set; }
    string FilterError { get; }

    /// <summary>The applied filters as chips, e.g. "Media 123-123_001", "Email", ".msg".</summary>
    IReadOnlyList<FilterChip> ActiveFilters { get; }

    /// <summary>Removes one filter; parameter: the chip's <see cref="FilterChip.Key"/>.</summary>
    ICommand RemoveFilterCommand { get; }

    /// <summary>Shows all media: leaves the media, folder or saved search (other filters stay).</summary>
    void ShowAllMedia();

    ICommand ApplyCommand { get; }
    ICommand ClearFiltersCommand { get; }

    // ---- Table ----

    /// <summary>The current page (replaced as a whole when a page loads).</summary>
    IReadOnlyList<FileRow> Rows { get; }

    FileRow? SelectedRow { get; set; }

    /// <summary>"default", "name", "extension", "size" or "modified".</summary>
    string SortKey { get; }

    bool SortDescending { get; }

    Task SortByAsync(string key);

    /// <summary>Keys of the optional columns shown (the Name column is always shown).</summary>
    IReadOnlySet<string> VisibleColumns { get; }

    void ToggleColumn(string key);

    // ---- Paging ----
    int PageIndex { get; }
    int PageSize { get; }
    IReadOnlyList<int> PageSizes { get; }
    long TotalCount { get; }
    bool IsCounting { get; }
    bool IsLoading { get; }

    /// <summary>"738,639 files · 3.12 TB".</summary>
    string TotalsText { get; }

    Task GoToPageAsync(int pageIndex);

    Task SetPageSizeAsync(int pageSize);

    // ---- Row actions (selected row) ----
    ICommand CopyPathCommand { get; }
    ICommand CopySha1Command { get; }
    ICommand OpenContainingFolderCommand { get; }
    ICommand ShowCopiesCommand { get; }

    /// <summary>Export current view: the Export dialog with the current filter as its scope (BRW-05).</summary>
    ICommand ExportViewCommand { get; }

    // ---- Copy (requirements 5.8b): the ticked rows, or all results when none are ticked ----
    bool CanCopyFiles { get; }

    /// <summary>Writes a .bat file with a copy command per file, and a CSV manifest.</summary>
    ICommand GenerateCopyBatchCommand { get; }

    /// <summary>Copies the files in the app, with a CSV manifest.</summary>
    ICommand CopyFilesCommand { get; }
}

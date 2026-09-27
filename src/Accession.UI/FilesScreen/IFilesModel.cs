using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.FilesScreen;

/// <summary>The Files screen (requirements 5.8, section 8.10): folder tree, filters and a paged file table.</summary>
public interface IFilesModel : INotifyPropertyChanged
{
    // ---- Tree ----
    ObservableCollection<FolderTreeNode> Folders { get; }
    FolderTreeNode? SelectedFolder { get; set; }

    // ---- Filters (applied with ApplyCommand) ----
    string NameContains { get; set; }
    string ExtensionText { get; set; }
    IReadOnlyList<SelectOption> MediaOptions { get; }
    string MediaValue { get; set; }
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

    /// <summary>Short labels of the applied filters, e.g. "Media 123-123_001", "Email", ".msg".</summary>
    IReadOnlyList<string> ActiveFilters { get; }

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
    ICommand RefreshCommand { get; }
}

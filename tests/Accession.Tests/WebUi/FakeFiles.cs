using System.Collections.ObjectModel;
using System.Windows.Input;
using Accession.UI.FilesScreen;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

/// <summary>Sample Files screen data.</summary>
internal sealed partial class FakeFiles : ObservableObject, IFilesModel
{
    public FakeFiles(int rows = 40, bool counting = false, bool savedSearchShown = false, int ticked = 0)
    {
        IsCounting = counting;
        SavedSearches.Add(new SavedSearchRow(1, "Privileged", "Emails with outside counsel", 1_204, "1,204 files", "2.31 GB",
            @"Created by LITSUPPORT\jane.doe on LIT-PC07, 2026-09-21 10:42"));
        SavedSearches.Add(new SavedSearchRow(2, "Board minutes 2021", string.Empty, 86, "86 files", "412.7 MB",
            @"Created by LITSUPPORT\john.roe on LIT-PC12, 2026-09-22 16:05"));
        SavedSearches.Add(new SavedSearchRow(3, "Hot documents", "For the first review round", 0, "0 files", "0 B"));
        if (savedSearchShown)
        {
            SideTab = "saved";
            SelectedSavedSearch = SavedSearches[0];
        }
        string[] folders = [@"\Users\jsmith\Mail\", @"\Shares\Finance\2021\Q3\", @"\Users\jsmith\Documents\Board\", @"\Engineering\CAD\"];
        string[] exts = ["msg", "xlsx", "pdf", "docx", "jpg"];
        var list = new List<FileRow>();
        for (var i = 0; i < rows; i++)
        {
            var ext = exts[i % exts.Length];
            var folder = folders[i % folders.Length];
            list.Add(new FileRow(i + 1, i % 3 == 0 ? "123-123_002" : "123-123_001", $"invoice_{2021000 + i}.{ext}", ext,
                ext switch { "msg" => "Email", "xlsx" => "Spreadsheets", "pdf" => "PDF", "jpg" => "Images", _ => "Documents" },
                folder, folder + $"invoice_{2021000 + i}.{ext}", $"{(i * 37 % 900) / 10.0:0.0} MB", "2021-03-02 10:14",
                $"2021-0{i % 9 + 1}-1{i % 10} 16:{i % 60:00}", "2026-09-14 16:02", i % 11 == 5 ? "Error" : "Hashed",
                i % 11 == 5 ? null : $"{i:x2}3f9c1a7be0d4c2f18a6b5e9d0c7a1b2e3f4a5b{i % 10}", i % 7 == 0 ? 3 : 1));
        }

        Rows = list;
        foreach (var row in list.Take(ticked))
        {
            _checked.Add(row.FileId);
        }

        SelectedRow = list.Count > 3 ? list[3] : null;
        Folders.Add(new FolderTreeNode(new FolderInfo(1, "123-123_001", true, false), _ => [
            new FolderInfo(11, "Users", true, false), new FolderInfo(12, "Shares", true, false), new FolderInfo(13, "Engineering", false, false),
            new FolderInfo(14, "OneDrive (link)", false, true)]) { IsExpanded = true });
        Folders.Add(new FolderTreeNode(new FolderInfo(2, "123-123_002", true, false), _ => []));
        SelectedFolder = Folders[0].Children[1];
    }

    private readonly HashSet<long> _checked = [];

    public ObservableCollection<FolderTreeNode> Folders { get; } = [];
    public FolderTreeNode? SelectedFolder { get; set; }
    public string SideTab { get; set; } = "folders";
    public ObservableCollection<SavedSearchRow> SavedSearches { get; } = [];
    public SavedSearchRow? SelectedSavedSearch { get; set; }
    public bool CanChangeSavedSearches => true;
    public ICommand NewSavedSearchCommand { get; } = new RelayCommand(() => { });
    public ICommand EditSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand DeleteSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand AddAllToSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand AddToSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand AddCheckedToSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand RemoveCheckedFromSavedSearchCommand { get; } = new RelayCommand(() => { });
    public ICommand RemoveFromSavedSearchCommand { get; } = new RelayCommand(() => { });
    public ICommand RemoveAllFromSavedSearchCommand { get; } = new RelayCommand(() => { });
    public IReadOnlySet<long> CheckedFileIds => _checked;
    public void SetChecked(FileRow row, bool isChecked) { }
    public void SetPageChecked(bool isChecked) { }
    public ICommand ClearCheckedCommand { get; } = new RelayCommand(() => { });
    public string NameContains { get; set; } = "invoice";
    public string ExtensionText { get; set; } = string.Empty;
    public IReadOnlyList<SelectOption> CategoryOptions { get; } = [new(string.Empty, "All categories"), new("1", "Email")];
    public string CategoryValue { get; set; } = string.Empty;
    public IReadOnlyList<SelectOption> HashStatusOptions { get; } = [new(string.Empty, "Any hash status")];
    public string HashStatusValue { get; set; } = string.Empty;
    public string MinSizeText { get; set; } = string.Empty;
    public string MaxSizeText { get; set; } = string.Empty;
    public string SizeUnitLabel => "MB";
    public string ModifiedFromText { get; set; } = string.Empty;
    public string ModifiedToText { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public bool DuplicatesOnly { get; set; }
    public bool ErrorsOnly { get; set; }
    public string Sha1Text { get; set; } = string.Empty;
    public string FilterError { get; set; } = string.Empty;
    public IReadOnlyList<FilterChip> ActiveFilters { get; } = [new(FilterKeys.Where, "In Shares"), new(FilterKeys.Name, "Name “invoice”")];
    public ICommand RemoveFilterCommand { get; } = new RelayCommand<object?>(_ => { });
    public void ShowAllMedia() { }
    public ICommand ApplyCommand { get; } = new RelayCommand(() => { });
    public ICommand ClearFiltersCommand { get; } = new RelayCommand(() => { });
    public IReadOnlyList<FileRow> Rows { get; }
    public FileRow? SelectedRow { get; set; }
    public string SortKey => "modified";
    public bool SortDescending => true;
    public Task SortByAsync(string key) => Task.CompletedTask;
    public IReadOnlySet<string> VisibleColumns { get; } = new HashSet<string> { "extension", "folder", "media", "size", "modified", "category", "hash", "copies" };
    public void ToggleColumn(string key)
    {
    }

    public int PageIndex => 2;
    public int PageSize => 1_000;
    public IReadOnlyList<int> PageSizes => Accession.UI.Components.Pager.FilePageSizes;
    public long TotalCount => 184_203;
    public bool IsCounting { get; }
    public bool IsLoading => false;
    public string TotalsText => IsCounting ? string.Empty : "184,203 files · 412.8 GB";
    public Task GoToPageAsync(int pageIndex) => Task.CompletedTask;
    public Task SetPageSizeAsync(int pageSize) => Task.CompletedTask;
    public ICommand CopyPathCommand { get; } = new RelayCommand(() => { });
    public ICommand CopySha1Command { get; } = new RelayCommand(() => { });
    public ICommand OpenContainingFolderCommand { get; } = new RelayCommand(() => { });
    public ICommand ShowCopiesCommand { get; } = new RelayCommand(() => { });
    public ICommand ExportViewCommand { get; } = new RelayCommand(() => { });
    public bool CanCopyFiles { get; set; } = true;
    public ICommand GenerateCopyBatchCommand { get; } = new RelayCommand(() => { });
    public ICommand CopyFilesCommand { get; } = new RelayCommand(() => { });
    public string ContextHeader => "report.pdf";
    public int ContextTargetCount { get; set; } = 1;
    public string ContextFolderName => "Shares";
    public void OpenFileMenu(FileRow row) { }
    public void OpenFolderMenu(FolderTreeNode node) { }
    public ICommand CopyTargetSha1sCommand { get; } = new RelayCommand(() => { });
    public ICommand CopyTargetPathsCommand { get; } = new RelayCommand(() => { });
    public ICommand CopyTargetNamesCommand { get; } = new RelayCommand(() => { });
    public ICommand QuickCopyCommand { get; } = new RelayCommand(() => { });
    public ICommand AddTargetsToSavedSearchCommand { get; } = new RelayCommand<object?>(_ => { });
    public ICommand FilterByExtensionCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenFolderInExplorerCommand { get; } = new RelayCommand(() => { });
    public ICommand CopyFolderPathCommand { get; } = new RelayCommand(() => { });
    public ICommand CopyFolderCommand { get; } = new RelayCommand(() => { });
    public ICommand ExportFolderCommand { get; } = new RelayCommand(() => { });
    public ICommand RefreshCommand { get; } = new RelayCommand(() => { });
}

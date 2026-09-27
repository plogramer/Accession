using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.Dashboard;

/// <summary>What the Dashboard page shows and can do (requirements 5.7, section 8.5). Implemented by the host application.</summary>
public interface IDashboardModel : INotifyPropertyChanged
{
    // Filter
    ObservableCollection<MediaFilterItem> MediaFilter { get; }
    string FilterText { get; }

    // State
    bool IsLoading { get; }
    bool IsLoadingDetails { get; }
    bool ScanInProgress { get; }
    string LoadError { get; }

    // Tiles
    string MediaCount { get; }
    string FolderCount { get; }
    string FileCount { get; }
    string TotalSize { get; }
    string HashedPercent { get; }
    string UniqueFiles { get; }
    string DuplicateFiles { get; }
    string DuplicateSize { get; }
    string DuplicateNote { get; }
    string ErrorCount { get; }

    // Sections
    ObservableCollection<DashboardMediaRowVm> ByMedia { get; }
    ObservableCollection<BarRow> ByCategory { get; }
    BarRow? SelectedCategory { get; set; }
    ObservableCollection<ExtensionRowVm> ByExtension { get; }
    string ExtensionSearch { get; set; }
    string ExtensionHeader { get; }
    ObservableCollection<BarRow> ByYear { get; }
    ObservableCollection<LargeFileRowVm> LargestFiles { get; }

    // Commands
    ICommand RefreshCommand { get; }
    ICommand SelectAllMediaCommand { get; }
    ICommand ClearCategoryCommand { get; }
    ICommand OpenMediaCommand { get; }
    ICommand OpenCategoryCommand { get; }
    ICommand OpenExtensionCommand { get; }
    ICommand OpenYearCommand { get; }
    ICommand OpenLargeFileCommand { get; }
    ICommand OpenDuplicatesCommand { get; }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Shell;

namespace Accession.UI.Dashboard;

/// <summary>What the Dashboard page shows and can do (requirements 5.7, section 8.5). Implemented by the host application.</summary>
public interface IDashboardModel : INotifyPropertyChanged, IRefreshableScreen
{
    // Filter
    ObservableCollection<MediaFilterItem> MediaFilter { get; }
    string FilterText { get; }

    /// <summary>Narrows <see cref="VisibleMediaFilter"/> by Media ID.</summary>
    string MediaSearch { get; set; }
    IReadOnlyList<MediaFilterItem> VisibleMediaFilter { get; }

    /// <summary>"12 of 140 selected".</summary>
    string SelectionSummary { get; }
    bool AllMediaSelected { get; }

    /// <summary>Nothing selected: the page shows a hint instead of empty sections.</summary>
    bool NoMediaSelected { get; }

    // State
    bool IsLoading { get; }
    bool IsLoadingDetails { get; }
    bool ScanInProgress { get; }
    string LoadError { get; }

    // Tiles
    string MediaCount { get; }
    string MediaNote { get; }
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
    /// <summary>Selects the media in <see cref="VisibleMediaFilter"/>.</summary>
    ICommand SelectAllMediaCommand { get; }

    /// <summary>Unselects the media in <see cref="VisibleMediaFilter"/>.</summary>
    ICommand UnselectAllMediaCommand { get; }

    /// <summary>Selects only the given <see cref="MediaFilterItem"/>.</summary>
    ICommand SelectOnlyMediaCommand { get; }

    /// <summary>All media, or none when all are selected (the By media header checkbox).</summary>
    ICommand ToggleAllMediaCommand { get; }
    ICommand ClearCategoryCommand { get; }
    ICommand OpenMediaCommand { get; }
    ICommand OpenCategoryCommand { get; }
    ICommand OpenExtensionCommand { get; }
    ICommand OpenYearCommand { get; }
    ICommand OpenLargeFileCommand { get; }
    ICommand OpenDuplicatesCommand { get; }
}

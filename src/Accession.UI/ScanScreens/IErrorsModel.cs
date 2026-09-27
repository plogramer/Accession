using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Shell;
using Accession.UI.FilesScreen;

namespace Accession.UI.ScanScreens;

/// <summary>Errors screen (requirements SCN-53, section 8.11): paged scan errors with filters.</summary>
public interface IErrorsModel : INotifyPropertyChanged, IRefreshableScreen
{
    IReadOnlyList<SelectOption> MediaFilterOptions { get; }
    string MediaFilterValue { get; set; }
    IReadOnlyList<SelectOption> ErrorTypeFilterOptions { get; }
    string ErrorTypeFilterValue { get; set; }

    /// <summary>Show Info entries (e.g. skipped reparse points).</summary>
    bool ShowInfo { get; set; }

    /// <summary>The current page.</summary>
    IReadOnlyList<ErrorRow> PageRows { get; }

    ErrorRow? SelectedRow { get; set; }

    int PageIndex { get; }
    int ErrorsPageSize { get; }
    IReadOnlyList<int> PageSizes { get; }
    long TotalCount { get; }

    Task GoToPageAsync(int pageIndex);

    Task SetPageSizeAsync(int pageSize);

    ICommand CopyPathCommand { get; }
    ICommand OpenContainingFolderCommand { get; }

    /// <summary>Hash again the failed files of the filtered media (or the selected error's media).</summary>
    ICommand RetryFailedCommand { get; }

}

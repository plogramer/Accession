using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.FilesScreen;

namespace Accession.UI.Records;

/// <summary>Audit Log screen (requirement AUD-04, section 8.13): read-only, newest first, paged.</summary>
public interface IAuditModel : INotifyPropertyChanged
{
    IReadOnlyList<SelectOption> ActionFilterOptions { get; }
    string ActionFilterValue { get; set; }
    IReadOnlyList<SelectOption> UserFilterOptions { get; }
    string UserFilterValue { get; set; }
    string MediaIdText { get; set; }

    /// <summary>yyyy-MM-dd (local date, inclusive) or empty.</summary>
    string FromDateText { get; set; }

    /// <summary>yyyy-MM-dd (local date, inclusive) or empty.</summary>
    string ToDateText { get; set; }

    IReadOnlyList<AuditRowVm> PageRows { get; }
    AuditRowVm? SelectedRow { get; set; }

    /// <summary>The selected entry's details as indented JSON.</summary>
    string Details { get; }

    int PageIndex { get; }
    int AuditPageSize { get; }
    IReadOnlyList<int> PageSizes { get; }
    long TotalCount { get; }

    Task GoToPageAsync(int pageIndex);

    Task SetPageSizeAsync(int pageSize);

    ICommand ApplyMediaFilterCommand { get; }
    ICommand ClearFiltersCommand { get; }
    ICommand RefreshCommand { get; }
}

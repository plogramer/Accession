using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.MediaScreen;

/// <summary>The Media screen (requirements 8.6): registered media, details, scan history and actions.</summary>
public interface IMediaModel : INotifyPropertyChanged
{
    ObservableCollection<MediaRowViewModel> Rows { get; }

    /// <summary>The media shown in the details panel.</summary>
    MediaRowViewModel? SelectedRow { get; set; }

    /// <summary>Media ticked for bulk actions; scan actions use these, or the selected media when none are ticked.</summary>
    void SetSelectedRows(IEnumerable<MediaRowViewModel> rows);

    string DetailsPath { get; }
    string DetailsAdded { get; }
    ObservableCollection<ScanHistoryRow> ScanHistory { get; }

    /// <summary>Media being scanned now, if any.</summary>
    long? ScanningMediaKey { get; }

    /// <summary>Live progress of the running scan, e.g. "Hashing 41,210 of 96,020 files · 212 MB/s · 4 min left".</summary>
    string LiveProgressText { get; }

    /// <summary>0–1 while hashing; null while counting files (progress unknown).</summary>
    double? LiveProgressRatio { get; }

    bool IsReadOnly { get; }

    ICommand AddMediaCommand { get; }
    ICommand DiscoverCommand { get; }
    ICommand DeleteCommand { get; }
    ICommand ScanCommand { get; }
    ICommand RescanCommand { get; }
    ICommand ResumeCommand { get; }
    ICommand RetryFailedCommand { get; }
    ICommand OpenInExplorerCommand { get; }
    ICommand OpenFilesCommand { get; }
    ICommand RefreshCommand { get; }
}

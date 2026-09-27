using System.Collections.ObjectModel;
using System.Windows.Input;
using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.UI.MediaScreen;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

/// <summary>Sample Media screen data.</summary>
internal sealed partial class FakeMedia : ObservableObject, IMediaModel
{
    public FakeMedia(bool empty = false, bool scanning = false, bool readOnly = false)
    {
        IsReadOnly = readOnly;
        if (empty)
        {
            return;
        }

        Add(1, "123-123_001", MediaStatus.Completed, 412_390, 1_840_000_000_000, 412_390, 0, 1);
        Add(2, "123-123_002", MediaStatus.CompletedWithErrors, 198_455, 912_600_000_000, 198_418, 37, 2);
        Add(3, "123-123_003", scanning ? MediaStatus.Hashing : MediaStatus.Completed, 96_020, 310_200_000_000, scanning ? 41_210 : 96_020, 0, 1);
        Add(4, "123-124_001", MediaStatus.Missing, 31_774, 54_900_000_000, 31_774, 2, 1);
        Add(5, "123-125_001", MediaStatus.New, 0, 0, 0, 0, 0);

        if (scanning)
        {
            ScanningMediaKey = 3;
            LiveProgressText = "Hashing 41,210 of 96,020 files · 212 MB/s · 9 min left";
            LiveProgressRatio = 0.42;
        }

        SelectedRow = Rows[scanning ? 2 : 1];
        ScanHistory.Add(new ScanHistoryRow(12, "RetryFailed", "2026-09-15 09:12", "2026-09-15 09:41", "CompletedWithErrors", "jane.doe on LIT-WS-011", "198,455"));
        ScanHistory.Add(new ScanHistoryRow(7, "Full", "2026-09-14 18:03", "2026-09-15 02:40", "CompletedWithErrors", "jane.doe on LIT-WS-011", "198,455"));
    }

    public ObservableCollection<MediaRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial MediaRowViewModel? SelectedRow { get; set; }

    public List<MediaRowViewModel> Ticked { get; } = [];

    public void SetSelectedRows(IEnumerable<MediaRowViewModel> rows)
    {
        Ticked.Clear();
        Ticked.AddRange(rows);
    }

    public string DetailsPath => @"\\evidence01\intake\NW-2026-0142\123-123_002";
    public string DetailsAdded => "Added 2026-09-10 11:02 by jane.doe";
    public ObservableCollection<ScanHistoryRow> ScanHistory { get; } = [];
    public long? ScanningMediaKey { get; }
    public string LiveProgressText { get; } = string.Empty;
    public double? LiveProgressRatio { get; }
    public bool IsReadOnly { get; }
    public ICommand AddMediaCommand { get; } = new RelayCommand(() => { });
    public ICommand DiscoverCommand { get; } = new RelayCommand(() => { });
    public ICommand DeleteCommand { get; } = new RelayCommand(() => { });
    public ICommand ScanCommand { get; } = new RelayCommand(() => { });
    public ICommand RescanCommand { get; } = new RelayCommand(() => { });
    public ICommand ResumeCommand { get; } = new RelayCommand(() => { }, () => false);
    public ICommand RetryFailedCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenInExplorerCommand { get; } = new RelayCommand(() => { });
    public ICommand OpenFilesCommand { get; } = new RelayCommand(() => { });
    public ICommand ExportCommand { get; } = new RelayCommand(() => { });
    public ICommand RefreshCommand { get; } = new RelayCommand(() => { });

    private void Add(long key, string id, MediaStatus status, long files, long bytes, long hashed, long errors, int scans) =>
        Rows.Add(new MediaRowViewModel(new Media
        {
            MediaKey = key,
            MediaId = id,
            Status = status,
            FileCount = files,
            FolderCount = files / 90,
            TotalBytes = bytes,
            HashedCount = hashed,
            ErrorCount = errors,
            ScanCount = scans,
            LastScanCompletedUtc = scans > 0 ? new DateTimeOffset(2026, 9, 10 + (int)key, 14, 2, 0, TimeSpan.Zero) : null,
        }, SizeUnitSystem.Decimal, DisplayTimeZone.Utc));
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.UI.Dashboard;

/// <summary>One media in the dashboard's media filter.</summary>
public sealed partial class MediaFilterItem(long mediaKey, string mediaId, bool isChecked) : ObservableObject
{
    public long MediaKey { get; } = mediaKey;

    public string MediaId { get; } = mediaId;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

public sealed partial class DashboardMediaRowVm : ObservableObject
{
    public required long MediaKey { get; init; }
    public required string MediaId { get; init; }
    public required string Status { get; init; }
    public required string Folders { get; init; }
    public required string Files { get; init; }
    public required string Size { get; init; }
    public required string Hashed { get; init; }
    public required string Errors { get; init; }
    public required string LastScanned { get; init; }
    public required int Scans { get; init; }
    public required long FileCount { get; init; }
    public required long TotalBytes { get; init; }

    /// <summary>Hashed files as a fraction of all files (0–1), for progress bars.</summary>
    public double HashedRatio { get; init; }

    /// <summary>Duplicates within the media; filled in when the background query finishes.</summary>
    [ObservableProperty]
    public partial string Duplicates { get; set; } = "…";
}

/// <summary>Scale shared by the dashboard views.</summary>
public static class DashboardScale
{
    /// <summary>Bar length of the largest value in <see cref="BarRow.BarLength"/>.</summary>
    public const double MaxBarLength = 220;
}

/// <summary>A labelled bar: category or year.</summary>
public sealed record BarRow(string Label, string Files, string Size, string PercentFiles, string PercentSize, double BarLength, long FileCount, long TotalBytes);

public sealed record ExtensionRowVm(string Extension, string Category, string Files, string Size, string PercentSize, long FileCount, long TotalBytes);

public sealed record LargeFileRowVm(long MediaKey, string MediaId, string RelativePath, string Size, string Modified, long SizeBytes);

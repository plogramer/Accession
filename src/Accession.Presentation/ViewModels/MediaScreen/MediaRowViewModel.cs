using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using MediaModel = Accession.Core.Model.Media;
using MediaStatus = Accession.Core.Model.MediaStatus;

namespace Accession.Presentation.ViewModels.MediaScreen;

/// <summary>One row of the Media screen grid, with display-ready values.</summary>
public sealed class MediaRowViewModel
{
    public MediaRowViewModel(MediaModel media, SizeUnitSystem sizeUnit, DisplayTimeZone timeZone)
    {
        Media = media;
        var scanned = media.ScanCount > 0 || media.FileCount > 0;
        Status = Humanize(media.Status);
        IsMissing = media.Status == MediaStatus.Missing;
        HasErrors = media.ErrorCount > 0;
        Folders = scanned ? media.FolderCount.ToString("N0", CultureInfo.CurrentCulture) : "—";
        Files = scanned ? media.FileCount.ToString("N0", CultureInfo.CurrentCulture) : "—";
        Size = scanned ? SizeFormatter.Format(media.TotalBytes, sizeUnit) : "—";
        Hashed = !scanned ? "—" : media.FileCount == 0 ? "100 %" : $"{Math.Floor(100d * media.HashedCount / media.FileCount):0} %";
        Errors = scanned ? media.ErrorCount.ToString("N0", CultureInfo.CurrentCulture) : "—";
        LastScanned = media.Status is MediaStatus.Scanning or MediaStatus.Hashing ? "running"
            : media.LastScanCompletedUtc is { } completed ? TimeFormatter.Format(completed, timeZone)
            : "never";
    }

    public MediaModel Media { get; }

    public string MediaId => Media.MediaId;

    public string Status { get; }

    public bool IsMissing { get; }

    public bool HasErrors { get; }

    public string Folders { get; }

    public string Files { get; }

    public string Size { get; }

    public string Hashed { get; }

    public string Errors { get; }

    public int Scans => Media.ScanCount;

    public string LastScanned { get; }

    // Sort keys for numeric columns.
    public long FolderCount => Media.FolderCount;

    public long FileCount => Media.FileCount;

    public long TotalBytes => Media.TotalBytes;

    public long ErrorCount => Media.ErrorCount;

    public static string Humanize(MediaStatus status) => status switch
    {
        MediaStatus.CompletedWithErrors => "Completed with errors",
        _ => status.ToString(),
    };
}

/// <summary>One entry of a media's scan history.</summary>
public sealed record ScanHistoryRow(long ScanId, string Type, string Started, string Ended, string Outcome, string User, string Files);

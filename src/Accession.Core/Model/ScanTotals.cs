namespace Accession.Core.Model;

/// <summary>Counts for a media or a scan run.</summary>
public sealed record ScanTotals(long FolderCount, long FileCount, long TotalBytes, long HashedCount, long ErrorCount)
{
    public static readonly ScanTotals Zero = new(0, 0, 0, 0, 0);
}

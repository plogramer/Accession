using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

/// <summary>Bulk operations over a media's scan results (folders, files, errors, summaries).</summary>
public sealed class ScanDataRepository(DbScope scope) : RepositoryBase(scope)
{
    /// <summary>
    /// Deletes all Folder, File, ScanError and MediaExtensionSummary rows of a media (rescan and delete media).
    /// ScanLog and AuditLog rows are kept.
    /// </summary>
    public void DeleteForMedia(long mediaKey) =>
        Connection.Execute(
            """
            DELETE FROM ScanError WHERE MediaKey = @mediaKey;
            DELETE FROM MediaExtensionSummary WHERE MediaKey = @mediaKey;
            DELETE FROM File WHERE MediaKey = @mediaKey;
            DELETE FROM Folder WHERE MediaKey = @mediaKey;
            """,
            new { mediaKey },
            Transaction);

    /// <summary>Counts folders, files, bytes, hashed files and errors (excluding Info) of a media from the detail tables.</summary>
    public ScanTotals ComputeTotals(long mediaKey) =>
        Connection.QuerySingle<ScanTotals>(
            """
            SELECT
                (SELECT COUNT(*) FROM Folder WHERE MediaKey = @mediaKey) AS FolderCount,
                (SELECT COUNT(*) FROM File WHERE MediaKey = @mediaKey) AS FileCount,
                (SELECT COALESCE(SUM(SizeBytes), 0) FROM File WHERE MediaKey = @mediaKey) AS TotalBytes,
                (SELECT COUNT(*) FROM File WHERE MediaKey = @mediaKey AND HashStatus = 1) AS HashedCount,
                (SELECT COUNT(*) FROM ScanError WHERE MediaKey = @mediaKey AND Severity <> 'Info') AS ErrorCount
            """,
            new { mediaKey },
            Transaction);
}

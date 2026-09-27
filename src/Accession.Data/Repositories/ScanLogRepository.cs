using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

public sealed class ScanLogRepository(DbScope scope) : RepositoryBase(scope)
{
    /// <summary>Inserts the start of a scan run and returns its ScanId.</summary>
    public long Start(ScanLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ScanId = Connection.ExecuteScalar<long>(
            """
            INSERT INTO ScanLog (MediaKey, MediaId, ScanType, StartedAtUtc, UserName, MachineName, AppVersion, EnumThreads, HashThreads)
            VALUES (@MediaKey, @MediaId, @ScanType, @StartedAtUtc, @UserName, @MachineName, @AppVersion, @EnumThreads, @HashThreads);
            SELECT last_insert_rowid();
            """,
            new
            {
                entry.MediaKey, entry.MediaId, ScanType = entry.ScanType.ToString(), entry.StartedAtUtc,
                entry.UserName, entry.MachineName, entry.AppVersion, entry.EnumThreads, entry.HashThreads,
            },
            Transaction);
        return entry.ScanId;
    }

    public void Finish(long scanId, DateTimeOffset endedAt, ScanOutcome outcome, ScanTotals totals, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Connection.Execute(
            """
            UPDATE ScanLog SET EndedAtUtc = @endedAt, Outcome = @outcome, FolderCount = @FolderCount, FileCount = @FileCount,
                TotalBytes = @TotalBytes, HashedCount = @HashedCount, ErrorCount = @ErrorCount, Notes = @notes
            WHERE ScanId = @scanId
            """,
            new
            {
                scanId, endedAt, outcome = outcome.ToString(), notes,
                totals.FolderCount, totals.FileCount, totals.TotalBytes, totals.HashedCount, totals.ErrorCount,
            },
            Transaction);
    }

    /// <summary>Scan history of a media, newest first.</summary>
    public IReadOnlyList<ScanLogEntry> ListByMedia(long mediaKey) =>
        Connection.Query<ScanLogEntry>(
            "SELECT * FROM ScanLog WHERE MediaKey = @mediaKey ORDER BY StartedAtUtc DESC, ScanId DESC",
            new { mediaKey },
            Transaction).AsList();

    /// <summary>Runs that never finished (e.g. after a crash).</summary>
    public IReadOnlyList<ScanLogEntry> ListUnfinished() =>
        Connection.Query<ScanLogEntry>("SELECT * FROM ScanLog WHERE EndedAtUtc IS NULL ORDER BY ScanId", transaction: Transaction).AsList();
}

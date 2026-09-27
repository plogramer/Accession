using Accession.Core.Model;
using Accession.Data.Repositories;
using Dapper;

namespace Accession.Data.Scanning;

/// <summary>
/// Cleans up after the application stopped during a scan (crash, power loss): media left Queued, Scanning,
/// Hashing or Paused become Incomplete (or New if nothing was scanned) and unfinished ScanLog rows are closed
/// as Interrupted (SCN-06).
/// </summary>
public static class ScanRecovery
{
    private static readonly MediaStatus[] Unfinished = [MediaStatus.Queued, MediaStatus.Scanning, MediaStatus.Hashing, MediaStatus.Paused];

    /// <returns>Number of media that were recovered.</returns>
    public static int Recover(DbScope scope, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var media = new MediaRepository(scope);
        var scanData = new ScanDataRepository(scope);
        var scanLog = new ScanLogRepository(scope);
        var recovered = 0;

        foreach (var item in media.ListActive().Where(m => Unfinished.Contains(m.Status)))
        {
            var hasData = scope.Connection.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM Folder WHERE MediaKey = @MediaKey", new { item.MediaKey }, scope.Transaction) > 0;
            var totals = scanData.ComputeTotals(item.MediaKey);
            media.UpdateTotals(item.MediaKey, totals);
            media.SetStatus(item.MediaKey, hasData || item.ScanCount > 0 ? MediaStatus.Incomplete : MediaStatus.New);
            recovered++;
        }

        foreach (var entry in scanLog.ListUnfinished())
        {
            scanLog.Finish(entry.ScanId, now, ScanOutcome.Interrupted, scanData.ComputeTotals(entry.MediaKey),
                "The application stopped before the scan finished.");
        }

        return recovered;
    }
}

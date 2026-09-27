using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

public sealed class MediaRepository(DbScope scope) : RepositoryBase(scope)
{
    /// <summary>Non-deleted media ordered by Media ID.</summary>
    public IReadOnlyList<Media> ListActive() =>
        Connection.Query<Media>("SELECT * FROM Media WHERE IsDeleted = 0 ORDER BY MediaId", transaction: Transaction).AsList();

    public Media? Get(long mediaKey) =>
        Connection.QuerySingleOrDefault<Media>("SELECT * FROM Media WHERE MediaKey = @mediaKey", new { mediaKey }, Transaction);

    /// <summary>The non-deleted media with this Media ID (case-insensitive), if any.</summary>
    public Media? FindActive(string mediaId) =>
        Connection.QuerySingleOrDefault<Media>(
            "SELECT * FROM Media WHERE MediaId = @mediaId AND IsDeleted = 0", new { mediaId }, Transaction);

    /// <summary>Media IDs that were deleted and are not currently active (for "previously deleted" in discovery).</summary>
    public IReadOnlySet<string> ListDeletedMediaIds() =>
        Connection.Query<string>(
                """
                SELECT DISTINCT d.MediaId FROM Media d
                WHERE d.IsDeleted = 1
                  AND NOT EXISTS (SELECT 1 FROM Media a WHERE a.IsDeleted = 0 AND a.MediaId = d.MediaId)
                """,
                transaction: Transaction)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a media with status New and returns its key.</summary>
    public long Insert(string mediaId, string relativePath, DateTimeOffset addedAt, string addedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return Connection.ExecuteScalar<long>(
            """
            INSERT INTO Media (MediaId, RelativePath, Status, AddedAtUtc, AddedBy)
            VALUES (@mediaId, @relativePath, @status, @addedAt, @addedBy);
            SELECT last_insert_rowid();
            """,
            new { mediaId, relativePath, status = nameof(MediaStatus.New), addedAt, addedBy },
            Transaction);
    }

    public void SetStatus(long mediaKey, MediaStatus status) =>
        Connection.Execute(
            "UPDATE Media SET Status = @status WHERE MediaKey = @mediaKey",
            new { mediaKey, status = status.ToString() },
            Transaction);

    /// <summary>Sets status Missing and remembers the previous status. No-op if already Missing.</summary>
    public void MarkMissing(long mediaKey) =>
        Connection.Execute(
            """
            UPDATE Media SET StatusBeforeMissing = Status, Status = @missing
            WHERE MediaKey = @mediaKey AND Status <> @missing
            """,
            new { mediaKey, missing = nameof(MediaStatus.Missing) },
            Transaction);

    /// <summary>Restores the status saved by <see cref="MarkMissing"/>. No-op unless the media is Missing.</summary>
    public void RestoreFromMissing(long mediaKey) =>
        Connection.Execute(
            """
            UPDATE Media SET Status = COALESCE(StatusBeforeMissing, @fallback), StatusBeforeMissing = NULL
            WHERE MediaKey = @mediaKey AND Status = @missing
            """,
            new { mediaKey, missing = nameof(MediaStatus.Missing), fallback = nameof(MediaStatus.New) },
            Transaction);

    /// <summary>Records the start of a scan run; full scans also increment <c>ScanCount</c>.</summary>
    public void MarkScanStarted(long mediaKey, DateTimeOffset startedAt, bool isFullScan) =>
        Connection.Execute(
            """
            UPDATE Media SET LastScanStartedUtc = @startedAt, ScanCount = ScanCount + @increment
            WHERE MediaKey = @mediaKey
            """,
            new { mediaKey, startedAt, increment = isFullScan ? 1 : 0 },
            Transaction);

    public void MarkScanCompleted(long mediaKey, DateTimeOffset completedAt) =>
        Connection.Execute(
            "UPDATE Media SET LastScanCompletedUtc = @completedAt WHERE MediaKey = @mediaKey",
            new { mediaKey, completedAt },
            Transaction);

    public void UpdateTotals(long mediaKey, ScanTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Connection.Execute(
            """
            UPDATE Media SET FolderCount = @FolderCount, FileCount = @FileCount, TotalBytes = @TotalBytes,
                HashedCount = @HashedCount, ErrorCount = @ErrorCount
            WHERE MediaKey = @MediaKey
            """,
            new { MediaKey = mediaKey, totals.FolderCount, totals.FileCount, totals.TotalBytes, totals.HashedCount, totals.ErrorCount },
            Transaction);
    }

    /// <summary>Marks the media deleted. Its scan data must be removed separately (<see cref="ScanDataRepository"/>).</summary>
    public void SoftDelete(long mediaKey, DateTimeOffset deletedAt, string deletedBy) =>
        Connection.Execute(
            "UPDATE Media SET IsDeleted = 1, DeletedAtUtc = @deletedAt, DeletedBy = @deletedBy WHERE MediaKey = @mediaKey",
            new { mediaKey, deletedAt, deletedBy },
            Transaction);
}

using System.Text;
using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

/// <summary>Filter and keyset page for the Errors screen. Results are ordered by ErrorId.</summary>
public sealed record ScanErrorQuery
{
    public long? MediaKey { get; init; }
    public IReadOnlyCollection<ScanErrorType>? ErrorTypes { get; init; }
    public IReadOnlyCollection<ScanErrorSeverity>? Severities { get; init; }

    /// <summary>Return rows with ErrorId greater than this (the last ErrorId of the previous page).</summary>
    public long? AfterErrorId { get; init; }

    public int PageSize { get; init; } = 500;
}

public sealed class ScanErrorRepository(DbScope scope) : RepositoryBase(scope)
{
    public long Insert(ScanErrorEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ErrorId = Connection.ExecuteScalar<long>(
            """
            INSERT INTO ScanError (MediaKey, ScanId, RelativePath, ItemType, ErrorType, Severity, ErrorCode, Message, OccurredAtUtc)
            VALUES (@MediaKey, @ScanId, @RelativePath, @ItemType, @ErrorType, @Severity, @ErrorCode, @Message, @OccurredAtUtc);
            SELECT last_insert_rowid();
            """,
            new
            {
                entry.MediaKey, entry.ScanId, entry.RelativePath, ItemType = entry.ItemType.ToString(),
                ErrorType = entry.ErrorType.ToString(), Severity = entry.Severity.ToString(),
                entry.ErrorCode, entry.Message, entry.OccurredAtUtc,
            },
            Transaction);
        return entry.ErrorId;
    }

    public IReadOnlyList<ScanErrorEntry> List(ScanErrorQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var sql = new StringBuilder("SELECT * FROM ScanError WHERE 1 = 1");
        var parameters = new DynamicParameters();
        if (query.MediaKey is not null)
        {
            sql.Append(" AND MediaKey = @MediaKey");
            parameters.Add("MediaKey", query.MediaKey);
        }

        if (query.ErrorTypes is { Count: > 0 })
        {
            sql.Append(" AND ErrorType IN @ErrorTypes");
            parameters.Add("ErrorTypes", query.ErrorTypes.Select(t => t.ToString()).ToList());
        }

        if (query.Severities is { Count: > 0 })
        {
            sql.Append(" AND Severity IN @Severities");
            parameters.Add("Severities", query.Severities.Select(s => s.ToString()).ToList());
        }

        if (query.AfterErrorId is not null)
        {
            sql.Append(" AND ErrorId > @AfterErrorId");
            parameters.Add("AfterErrorId", query.AfterErrorId);
        }

        sql.Append(" ORDER BY ErrorId LIMIT @PageSize");
        parameters.Add("PageSize", query.PageSize);

        return Connection.Query<ScanErrorEntry>(sql.ToString(), parameters, Transaction).AsList();
    }

    /// <summary>Error count for a media; Info entries (e.g. skipped reparse points) are excluded unless requested.</summary>
    public long CountByMedia(long mediaKey, bool includeInfo = false) =>
        Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM ScanError WHERE MediaKey = @mediaKey AND (@includeInfo OR Severity <> 'Info')",
            new { mediaKey, includeInfo },
            Transaction);
}

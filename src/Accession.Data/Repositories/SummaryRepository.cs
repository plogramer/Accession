using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

/// <summary><c>MediaExtensionSummary</c>: per media, per extension file counts and bytes (drives the dashboard).</summary>
public sealed class SummaryRepository(DbScope scope) : RepositoryBase(scope)
{
    /// <summary>Recomputes the summary rows of a media from its File rows.</summary>
    public void RebuildForMedia(long mediaKey) =>
        Connection.Execute(
            """
            DELETE FROM MediaExtensionSummary WHERE MediaKey = @mediaKey;
            INSERT INTO MediaExtensionSummary (MediaKey, Extension, FileCount, TotalBytes)
            SELECT MediaKey, lower(Extension), COUNT(*), SUM(SizeBytes)
            FROM File WHERE MediaKey = @mediaKey
            GROUP BY MediaKey, lower(Extension);
            """,
            new { mediaKey },
            Transaction);

    /// <summary>Summary rows for the given media, or for all non-deleted media when <paramref name="mediaKeys"/> is null.</summary>
    public IReadOnlyList<ExtensionSummary> List(IReadOnlyCollection<long>? mediaKeys = null)
    {
        const string select =
            """
            SELECT s.MediaKey, s.Extension, s.FileCount, s.TotalBytes
            FROM MediaExtensionSummary s JOIN Media m ON m.MediaKey = s.MediaKey AND m.IsDeleted = 0
            """;

        return mediaKeys is null
            ? Connection.Query<ExtensionSummary>(select + " ORDER BY s.MediaKey, s.Extension", transaction: Transaction).AsList()
            : Connection.Query<ExtensionSummary>(
                select + " WHERE s.MediaKey IN @mediaKeys ORDER BY s.MediaKey, s.Extension", new { mediaKeys }, Transaction).AsList();
    }
}

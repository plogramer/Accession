using Accession.Data.Schema;
using Dapper;

namespace Accession.Data.Browsing;

public sealed class CategoryCount
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class ExtensionCount
{
    public string Extension { get; init; } = string.Empty;
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }

    /// <summary>True when the extension is in the application's mapping (false for Other / No Extension).</summary>
    public bool IsMapped { get; init; }
}

/// <summary>Categories screen (requirement CAT-02): categories with counts and their extensions.</summary>
public sealed class CategoryQueries
{
    public IReadOnlyList<CategoryCount> Categories(InventoryDatabase database)
    {
        using var scope = database.Open();
        return scope.Connection.Query<CategoryCount>(
            $"""
            WITH s AS (
                SELECT cat.CategoryId, SUM(x.FileCount) AS FileCount, SUM(x.TotalBytes) AS TotalBytes
                FROM MediaExtensionSummary x
                JOIN Media m ON m.MediaKey = x.MediaKey AND m.IsDeleted = 0
                {CategorySql.JoinCategory("x.Extension", "cat")}
                GROUP BY cat.CategoryId
            )
            SELECT c.CategoryId, c.Name, c.Description, c.SortOrder,
                   COALESCE(s.FileCount, 0) AS FileCount, COALESCE(s.TotalBytes, 0) AS TotalBytes
            FROM FileCategory c LEFT JOIN s ON s.CategoryId = c.CategoryId
            ORDER BY c.SortOrder
            """).AsList();
    }

    /// <summary>
    /// Extensions of a category with counts in this inventory. Mapped categories list every mapped extension
    /// (0 when none found); Other / Unknown and No Extension list the extensions actually found.
    /// </summary>
    public IReadOnlyList<ExtensionCount> Extensions(InventoryDatabase database, int categoryId)
    {
        using var scope = database.Open();
        return scope.Connection.Query<ExtensionCount>(
            $"""
            WITH found AS (
                SELECT x.Extension, SUM(x.FileCount) AS FileCount, SUM(x.TotalBytes) AS TotalBytes
                FROM MediaExtensionSummary x
                JOIN Media m ON m.MediaKey = x.MediaKey AND m.IsDeleted = 0
                {CategorySql.JoinCategory("x.Extension", "cat")}
                WHERE cat.CategoryId = @categoryId
                GROUP BY x.Extension
            )
            SELECT ec.Extension, COALESCE(found.FileCount, 0) AS FileCount, COALESCE(found.TotalBytes, 0) AS TotalBytes, 1 AS IsMapped
            FROM ExtensionCategory ec LEFT JOIN found ON found.Extension = ec.Extension
            WHERE ec.CategoryId = @categoryId
            UNION ALL
            SELECT found.Extension, found.FileCount, found.TotalBytes, 0 AS IsMapped
            FROM found
            WHERE NOT EXISTS (SELECT 1 FROM ExtensionCategory ec WHERE ec.Extension = found.Extension)
            ORDER BY FileCount DESC, Extension
            """,
            new { categoryId }).AsList();
    }
}

using System.Text;
using Accession.Core.Model;
using Accession.Core.Time;
using Accession.Data.Schema;
using Dapper;

namespace Accession.Data.Browsing;

/// <summary>
/// Folder tree and file queries for the File browser (requirements 5.8). Files are paged with keyset
/// pagination, so paging stays fast on millions of rows; only "go to page N" falls back to OFFSET.
/// </summary>
public sealed class FileBrowserQueries
{
    public const int DefaultPageSize = 500;

    /// <summary>Top folder (the media folder itself) of each active media.</summary>
    public IReadOnlyList<FolderNode> MediaRoots(InventoryDatabase database)
    {
        using var scope = database.Open();
        return scope.Connection.Query<FolderNode>(
            """
            SELECT fo.FolderId, fo.MediaKey, m.MediaId AS Name, fo.RelativePath, fo.IsReparsePoint,
                   EXISTS (SELECT 1 FROM Folder c WHERE c.ParentFolderId = fo.FolderId) AS HasChildren
            FROM Folder fo JOIN Media m ON m.MediaKey = fo.MediaKey AND m.IsDeleted = 0
            WHERE fo.ParentFolderId IS NULL
            ORDER BY m.MediaId COLLATE NOCASE
            """).AsList();
    }

    public IReadOnlyList<FolderNode> ChildFolders(InventoryDatabase database, long parentFolderId)
    {
        using var scope = database.Open();
        return scope.Connection.Query<FolderNode>(
            """
            SELECT fo.FolderId, fo.MediaKey, fo.Name, fo.RelativePath, fo.IsReparsePoint,
                   EXISTS (SELECT 1 FROM Folder c WHERE c.ParentFolderId = fo.FolderId) AS HasChildren
            FROM Folder fo
            WHERE fo.ParentFolderId = @parentFolderId
            ORDER BY fo.Name COLLATE NOCASE
            """,
            new { parentFolderId }).AsList();
    }

    /// <summary>Rows after <paramref name="after"/> (or the first rows), in sort order. Keyset: fast at any depth.</summary>
    public FilePage Page(InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending,
        FilePageCursor? after, int pageSize = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        var items = Fetch(database, filter, sort, descending, after, backwards: false, pageSize, offset: 0, cancellationToken);
        var next = items.Count == pageSize ? CursorOf(items[^1], sort) : null;
        return new FilePage(items, next);
    }

    /// <summary>The <paramref name="pageSize"/> rows just before <paramref name="before"/>, in sort order. Keyset.</summary>
    public IReadOnlyList<FileItem> PageBefore(InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending,
        FilePageCursor before, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        var items = Fetch(database, filter, sort, descending, before, backwards: true, pageSize, offset: 0, cancellationToken);
        items.Reverse();
        return items;
    }

    /// <summary>The last <paramref name="count"/> rows, in sort order (read from the end, so it is fast).</summary>
    public IReadOnlyList<FileItem> LastRows(InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending,
        int count, CancellationToken cancellationToken = default)
    {
        var items = Fetch(database, filter, sort, descending, boundary: null, backwards: true, count, offset: 0, cancellationToken);
        items.Reverse();
        return items;
    }

    /// <summary>Rows starting at <paramref name="offset"/>. Uses OFFSET: slower deep into large results; used for "go to page".</summary>
    public IReadOnlyList<FileItem> PageAtOffset(InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending,
        long offset, int pageSize, CancellationToken cancellationToken = default) =>
        Fetch(database, filter, sort, descending, boundary: null, backwards: false, pageSize, offset, cancellationToken);

    /// <summary>Keyset cursor for continuing after <paramref name="item"/>.</summary>
    public static FilePageCursor CursorOf(FileItem item, FileSortColumn sort)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new FilePageCursor(SortValue(item, sort), item.FileId);
    }

    private List<FileItem> Fetch(InventoryDatabase database, FileFilter filter, FileSortColumn sort, bool descending,
        FilePageCursor? boundary, bool backwards, int limit, long offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var scope = database.Open();
        var (where, parameters) = BuildWhere(scope, filter);
        var sortExpression = SortExpression(sort);

        // Reading backwards = the opposite order; callers reverse the rows afterwards.
        var effectiveDescending = descending != backwards;
        var direction = effectiveDescending ? "DESC" : "ASC";
        var comparison = effectiveDescending ? "<" : ">";

        if (boundary is not null)
        {
            if (sort == FileSortColumn.Default)
            {
                where.Append($" AND f.FileId {comparison} @afterId");
            }
            else
            {
                where.Append($" AND ({sortExpression} {comparison} @afterValue OR ({sortExpression} = @afterValue AND f.FileId {comparison} @afterId))");
                parameters.Add("afterValue", boundary.SortValue);
            }

            parameters.Add("afterId", boundary.FileId);
        }

        parameters.Add("limit", limit);
        parameters.Add("offset", offset);
        var order = sort == FileSortColumn.Default ? $"f.FileId {direction}" : $"{sortExpression} {direction}, f.FileId {direction}";
        var sql =
            $"""
            SELECT f.FileId, f.MediaKey, m.MediaId, f.FolderId, fo.RelativePath AS FolderPath, f.Name, f.Extension,
                   cat.Name AS Category, f.SizeBytes, f.CreatedUtc, f.ModifiedUtc, f.AccessedUtc, f.Sha1, f.HashStatus,
                   CASE WHEN f.Sha1 IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM File d WHERE d.Sha1 = f.Sha1) END AS DuplicateCount
            FROM File f
            JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
            JOIN Folder fo ON fo.FolderId = f.FolderId
            {CategorySql.JoinCategory("f.Extension", "cat")}
            WHERE {where}
            ORDER BY {order}
            LIMIT @limit OFFSET @offset
            """;

        return scope.Connection.Query<FileItem>(new CommandDefinition(sql, parameters, commandTimeout: 0, cancellationToken: cancellationToken)).AsList();
    }

    /// <summary>File count and total size matching <paramref name="filter"/> (for the footer).</summary>
    public FileTotals Totals(InventoryDatabase database, FileFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var scope = database.Open();
        var (where, parameters) = BuildWhere(scope, filter);
        var needsCategory = filter.CategoryId is not null;
        var sql =
            $"""
            SELECT COUNT(*) AS FileCount, COALESCE(SUM(f.SizeBytes), 0) AS TotalBytes
            FROM File f
            JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
            JOIN Folder fo ON fo.FolderId = f.FolderId
            {(needsCategory ? CategorySql.JoinCategory("f.Extension", "cat") : string.Empty)}
            WHERE {where}
            """;
        return scope.Connection.QuerySingle<FileTotals>(new CommandDefinition(sql, parameters, commandTimeout: 0, cancellationToken: cancellationToken));
    }

    private static (StringBuilder Where, DynamicParameters Parameters) BuildWhere(DbScope scope, FileFilter filter)
    {
        var where = new StringBuilder("1 = 1");
        var p = new DynamicParameters();

        if (filter.MediaKey is { } mediaKey)
        {
            where.Append(" AND f.MediaKey = @mediaKey");
            p.Add("mediaKey", mediaKey);
        }

        if (filter.FolderId is { } folderId)
        {
            if (filter.IncludeSubfolders)
            {
                var folder = scope.Connection.QuerySingleOrDefault<(long MediaKey, string RelativePath)>(
                    "SELECT MediaKey, RelativePath FROM Folder WHERE FolderId = @folderId", new { folderId });
                where.Append(" AND f.MediaKey = @folderMedia AND fo.RelativePath LIKE @folderPrefix ESCAPE '^'");
                p.Add("folderMedia", folder.MediaKey);
                p.Add("folderPrefix", EscapeLike(folder.RelativePath ?? string.Empty) + "%");
            }
            else
            {
                where.Append(" AND f.FolderId = @folderId");
                p.Add("folderId", folderId);
            }
        }

        if (filter.CategoryId is { } categoryId)
        {
            where.Append(" AND cat.CategoryId = @categoryId");
            p.Add("categoryId", categoryId);
        }

        if (filter.Extension is { } extension)
        {
            where.Append(" AND f.Extension = @extension");
            p.Add("extension", extension.Trim().TrimStart('.').ToLowerInvariant());
        }

        if (filter.MinSize is { } min)
        {
            where.Append(" AND f.SizeBytes >= @minSize");
            p.Add("minSize", min);
        }

        if (filter.MaxSize is { } max)
        {
            where.Append(" AND f.SizeBytes <= @maxSize");
            p.Add("maxSize", max);
        }

        if (filter.ModifiedFrom is { } from)
        {
            where.Append(" AND f.ModifiedUtc >= @modifiedFrom");
            p.Add("modifiedFrom", UtcTimestamp.ToText(from));
        }

        if (filter.ModifiedTo is { } to)
        {
            where.Append(" AND f.ModifiedUtc < @modifiedTo");
            p.Add("modifiedTo", UtcTimestamp.ToText(to));
        }

        if (filter.HashStatus is { } status)
        {
            where.Append(" AND f.HashStatus = @hashStatus");
            p.Add("hashStatus", (int)status);
        }

        if (filter.DuplicatesOnly)
        {
            where.Append(" AND f.Sha1 IN (SELECT Sha1 FROM File WHERE Sha1 IS NOT NULL GROUP BY Sha1 HAVING COUNT(*) > 1)");
        }

        if (filter.ErrorsOnly)
        {
            where.Append(
                """
                 AND (f.HashStatus = 2 OR EXISTS (SELECT 1 FROM ScanError e
                      WHERE e.MediaKey = f.MediaKey AND e.ItemType = 'File' AND e.Severity <> 'Info'
                        AND e.RelativePath = fo.RelativePath || f.Name))
                """);
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            where.Append(" AND f.Name LIKE @name ESCAPE '^'");
            p.Add("name", "%" + EscapeLike(filter.NameContains.Trim()) + "%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Sha1))
        {
            where.Append(" AND f.Sha1 = @sha1");
            p.Add("sha1", filter.Sha1.Trim().ToLowerInvariant());
        }

        return (where, p);
    }

    private static string SortExpression(FileSortColumn sort) => sort switch
    {
        FileSortColumn.Name => "f.Name",
        FileSortColumn.Extension => "f.Extension",
        FileSortColumn.Size => "f.SizeBytes",
        FileSortColumn.Modified => "COALESCE(f.ModifiedUtc, '')",
        _ => "f.FileId",
    };

    private static object? SortValue(FileItem item, FileSortColumn sort) => sort switch
    {
        FileSortColumn.Name => item.Name,
        FileSortColumn.Extension => item.Extension,
        FileSortColumn.Size => item.SizeBytes,
        FileSortColumn.Modified => item.ModifiedUtc is { } m ? UtcTimestamp.ToText(m) : string.Empty,
        _ => item.FileId,
    };

    private static string EscapeLike(string value) =>
        value.Replace("^", "^^", StringComparison.Ordinal).Replace("%", "^%", StringComparison.Ordinal).Replace("_", "^_", StringComparison.Ordinal);
}

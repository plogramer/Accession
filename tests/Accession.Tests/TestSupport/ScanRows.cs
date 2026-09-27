using Accession.Data;
using Dapper;

namespace Accession.Tests.TestSupport;

/// <summary>Inserts Folder/File rows directly for repository tests (the scan engine writes these in production).</summary>
public static class ScanRows
{
    public static long AddFolder(DbScope scope, long mediaKey, string relativePath, long? parentId = null) =>
        scope.Connection.ExecuteScalar<long>(
            """
            INSERT INTO Folder (MediaKey, ParentFolderId, Name, RelativePath, IsEnumerated)
            VALUES (@mediaKey, @parentId, @name, @relativePath, 1);
            SELECT last_insert_rowid();
            """,
            new { mediaKey, parentId, name = relativePath.TrimEnd('\\').Split('\\')[^1], relativePath },
            scope.Transaction);

    public static long AddFile(DbScope scope, long mediaKey, long folderId, string name, long size, string? sha1 = null)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot < 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
        return scope.Connection.ExecuteScalar<long>(
            """
            INSERT INTO File (MediaKey, FolderId, Name, Extension, SizeBytes, Sha1, HashStatus)
            VALUES (@mediaKey, @folderId, @name, @extension, @size, @sha1, @hashStatus);
            SELECT last_insert_rowid();
            """,
            new { mediaKey, folderId, name, extension, size, sha1, hashStatus = sha1 is null ? 0 : 1 },
            scope.Transaction);
    }
}

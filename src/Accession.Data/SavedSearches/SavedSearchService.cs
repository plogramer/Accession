using Accession.Core.Model;
using Accession.Core.Time;
using Accession.Data.Browsing;
using Accession.Data.Schema;
using Accession.Data.Sessions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.SavedSearches;

/// <summary>A saved search with the number and size of its files that exist now.</summary>
public sealed class SavedSearchInfo
{
    public long SavedSearchId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset ModifiedAtUtc { get; init; }
    public string ModifiedBy { get; init; } = string.Empty;
}

/// <summary>Another saved search already has this name.</summary>
public sealed class SavedSearchNameTakenException(string name)
    : InvalidOperationException($"A saved search named '{name}' already exists.");

/// <summary>
/// Saved searches: named, fixed lists of files kept in the inventory. Files are remembered by media, folder path and
/// name, so they stay in the list after a rescan. Every change is audited; a read-only inventory cannot be changed.
/// </summary>
public sealed class SavedSearchService(InventorySessionFactory factory)
{
    public const int MaxNameLength = 100;

    /// <summary>All saved searches by name, with the count and size of their files.</summary>
    public IReadOnlyList<SavedSearchInfo> List(InventoryDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        using var scope = database.Open();
        return scope.Connection.Query<SavedSearchInfo>(new CommandDefinition(
            """
            SELECT s.SavedSearchId, s.Name, s.Description, s.CreatedAtUtc, s.CreatedBy, s.ModifiedAtUtc, s.ModifiedBy,
                   COUNT(f.FileId) AS FileCount, COALESCE(SUM(f.SizeBytes), 0) AS TotalBytes
            FROM SavedSearch s
            LEFT JOIN SavedSearchFile sf ON sf.SavedSearchId = s.SavedSearchId
            LEFT JOIN Media m ON m.MediaKey = sf.MediaKey AND m.IsDeleted = 0
            LEFT JOIN Folder fo ON fo.MediaKey = m.MediaKey AND fo.RelativePath = sf.FolderPath
            LEFT JOIN File f ON f.FolderId = fo.FolderId AND f.Name = sf.Name
            GROUP BY s.SavedSearchId
            ORDER BY s.Name
            """, commandTimeout: 0, cancellationToken: cancellationToken)).AsList();
    }

    /// <summary>The problem with a name, or empty when it is fine (uniqueness is checked when saving).</summary>
    public static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "Enter a name.";
        }

        if (trimmed.Length > MaxNameLength)
        {
            return $"Use at most {MaxNameLength} characters.";
        }

        return trimmed.Any(char.IsControl) ? "The name cannot contain control characters." : string.Empty;
    }

    /// <summary>Creates an empty saved search and returns its id.</summary>
    /// <exception cref="SavedSearchNameTakenException">The name is in use.</exception>
    public long Create(InventorySession session, string name, string? description)
    {
        var (cleanName, cleanDescription) = Clean(name, description);
        return Change(session, scope =>
        {
            var now = UtcTimestamp.ToText(factory.TimeProvider.GetUtcNow());
            var id = SaveName(cleanName, () => scope.Connection.ExecuteScalar<long>(
                """
                INSERT INTO SavedSearch (Name, Description, CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy)
                VALUES (@cleanName, @cleanDescription, @now, @user, @now, @user);
                SELECT last_insert_rowid();
                """,
                new { cleanName, cleanDescription, now, user = session.UserName }, scope.Transaction));
            session.Audit.Write(scope, AuditAction.SavedSearchCreated, details: new { SavedSearchId = id, Name = cleanName, Description = cleanDescription });
            return id;
        });
    }

    /// <summary>Renames a saved search and/or changes its description.</summary>
    public void Update(InventorySession session, long savedSearchId, string name, string? description)
    {
        var (cleanName, cleanDescription) = Clean(name, description);
        Change(session, scope =>
        {
            var before = Get(scope, savedSearchId);
            SaveName(cleanName, () => scope.Connection.Execute(
                """
                UPDATE SavedSearch SET Name = @cleanName, Description = @cleanDescription, ModifiedAtUtc = @now, ModifiedBy = @user
                WHERE SavedSearchId = @savedSearchId
                """,
                new { cleanName, cleanDescription, now = UtcTimestamp.ToText(factory.TimeProvider.GetUtcNow()), user = session.UserName, savedSearchId },
                scope.Transaction));
            session.Audit.Write(scope, AuditAction.SavedSearchChanged, details: new
            {
                SavedSearchId = savedSearchId,
                OldName = before.Name,
                Name = cleanName,
                OldDescription = before.Description,
                Description = cleanDescription,
            });
            return 0;
        });
    }

    /// <summary>Deletes a saved search (the files themselves are not touched).</summary>
    public void Delete(InventorySession session, long savedSearchId) =>
        Change(session, scope =>
        {
            var before = Get(scope, savedSearchId);
            var files = scope.Connection.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM SavedSearchFile WHERE SavedSearchId = @savedSearchId", new { savedSearchId }, scope.Transaction);
            scope.Connection.Execute("DELETE FROM SavedSearch WHERE SavedSearchId = @savedSearchId", new { savedSearchId }, scope.Transaction);
            session.Audit.Write(scope, AuditAction.SavedSearchDeleted, details: new { SavedSearchId = savedSearchId, before.Name, Files = files });
            return 0;
        });

    /// <summary>
    /// Adds every file matching <paramref name="filter"/> (all results of the Files screen, or selected rows via
    /// <see cref="FileFilter.FileIds"/>) in one statement. Files already in the list are skipped. Returns the number added.
    /// </summary>
    public long AddFiles(InventorySession session, long savedSearchId, FileFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Change(session, scope =>
        {
            var info = Get(scope, savedSearchId);
            var (where, parameters) = FileBrowserQueries.BuildWhere(scope, filter);
            parameters.Add("targetId", savedSearchId);
            parameters.Add("now", UtcTimestamp.ToText(factory.TimeProvider.GetUtcNow()));
            parameters.Add("user", session.UserName);
            var added = scope.Connection.Execute(new CommandDefinition(
                $"""
                INSERT OR IGNORE INTO SavedSearchFile (SavedSearchId, MediaKey, FolderPath, Name, AddedAtUtc, AddedBy)
                SELECT @targetId, f.MediaKey, fo.RelativePath, f.Name, @now, @user
                FROM File f
                JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
                JOIN Folder fo ON fo.FolderId = f.FolderId
                {CategorySql.JoinCategory("f.Extension", "cat")}
                WHERE {where}
                """, parameters, scope.Transaction, commandTimeout: 0, cancellationToken: cancellationToken));
            Touch(scope, savedSearchId, session.UserName);
            session.Audit.Write(scope, AuditAction.SavedSearchFilesAdded, details: new
            {
                SavedSearchId = savedSearchId, info.Name, Added = added, Selection = filter.FileIds is null ? "AllResults" : "SelectedRows",
            });
            return (long)added;
        });
    }

    /// <summary>Removes the files matching <paramref name="filter"/> from the saved search. Returns the number removed.</summary>
    public long RemoveFiles(InventorySession session, long savedSearchId, FileFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Change(session, scope =>
        {
            var info = Get(scope, savedSearchId);
            var (where, parameters) = FileBrowserQueries.BuildWhere(scope, filter);
            parameters.Add("targetId", savedSearchId);
            var removed = scope.Connection.Execute(new CommandDefinition(
                $"""
                DELETE FROM SavedSearchFile
                WHERE SavedSearchId = @targetId AND (MediaKey, FolderPath, Name) IN (
                    SELECT f.MediaKey, fo.RelativePath, f.Name
                    FROM File f
                    JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
                    JOIN Folder fo ON fo.FolderId = f.FolderId
                    {CategorySql.JoinCategory("f.Extension", "cat")}
                    WHERE {where})
                """, parameters, scope.Transaction, commandTimeout: 0, cancellationToken: cancellationToken));
            Touch(scope, savedSearchId, session.UserName);
            session.Audit.Write(scope, AuditAction.SavedSearchFilesRemoved, details: new
            {
                SavedSearchId = savedSearchId, info.Name, Removed = removed, Selection = filter.FileIds is null ? "AllResults" : "SelectedRows",
            });
            return (long)removed;
        });
    }

    private static (string Name, string? Description) Clean(string name, string? description)
    {
        var error = ValidateName(name);
        if (error.Length > 0)
        {
            throw new ArgumentException(error, nameof(name));
        }

        var trimmedDescription = description?.Trim();
        return (name.Trim(), string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription);
    }

    private static T Change<T>(InventorySession session, Func<DbScope, T> change)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsReadOnly)
        {
            throw new InvalidOperationException("The inventory is open read-only, so saved searches cannot be changed.");
        }

        using var scope = session.Database.Open();
        using var transaction = scope.BeginTransaction();
        var result = change(scope);
        transaction.Commit();
        return result;
    }

    /// <summary>Runs an insert or update that sets the name, turning a unique-name violation into a clear error.</summary>
    private static T SaveName<T>(string name, Func<T> write)
    {
        try
        {
            return write();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT: UX_SavedSearch_Name
        {
            throw new SavedSearchNameTakenException(name);
        }
    }

    private static NameRow Get(DbScope scope, long savedSearchId) =>
        scope.Connection.QuerySingleOrDefault<NameRow>(
            "SELECT Name, Description FROM SavedSearch WHERE SavedSearchId = @savedSearchId", new { savedSearchId }, scope.Transaction)
        ?? throw new InvalidOperationException("The saved search no longer exists.");

    private void Touch(DbScope scope, long savedSearchId, string user) =>
        scope.Connection.Execute(
            "UPDATE SavedSearch SET ModifiedAtUtc = @now, ModifiedBy = @user WHERE SavedSearchId = @savedSearchId",
            new { now = UtcTimestamp.ToText(factory.TimeProvider.GetUtcNow()), user, savedSearchId }, scope.Transaction);

    private sealed class NameRow
    {
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
    }
}

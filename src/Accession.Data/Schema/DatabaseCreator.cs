using Accession.Core.Model;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Schema;

/// <summary>Creates a new, empty inventory file with the current schema (requirement INV-04).</summary>
public static class DatabaseCreator
{
    /// <summary>
    /// Creates <paramref name="path"/> and writes the schema, the <c>InventoryConfig</c> row, the unlocked
    /// <c>InventoryLock</c> row and the <c>SchemaMigration</c> row. Nothing is left behind if creation fails.
    /// </summary>
    /// <param name="config">Matter and creator details. <see cref="InventoryConfig.SchemaVersion"/> and
    /// <see cref="InventoryConfig.LastDbPath"/> are set by this method.</param>
    public static void Create(string path, InventoryConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        path = Path.GetFullPath(path);
        if (File.Exists(path))
        {
            throw new IOException($"The file '{path}' already exists.");
        }

        config.SchemaVersion = InventorySchema.CurrentVersion;
        config.LastDbPath = path;

        try
        {
            using var connection = SqliteConnectionFactory.Open(path, SqliteOpenMode.ReadWriteCreate);

            // page_size only takes effect before the first table is created.
            connection.Execute("PRAGMA page_size = 8192;");

            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(InventorySchema.LoadScript("V001_Initial.sql"), transaction: transaction);
            connection.Execute(
                """
                INSERT INTO InventoryConfig (ConfigId, InventoryGuid, SchemaVersion, RootPath, ClientName, ClientCode,
                    MatterName, MatterCode, Description, MatterUrl, CreatedAtUtc, CreatedBy, CreatedOnMachine,
                    CreatedAppVersion, LastOpenedAtUtc, LastOpenedBy, LastDbPath)
                VALUES (1, @InventoryGuid, @SchemaVersion, @RootPath, @ClientName, @ClientCode,
                    @MatterName, @MatterCode, @Description, @MatterUrl, @CreatedAtUtc, @CreatedBy, @CreatedOnMachine,
                    @CreatedAppVersion, @LastOpenedAtUtc, @LastOpenedBy, @LastDbPath);
                INSERT INTO InventoryLock (LockId, IsLocked) VALUES (1, 0);
                INSERT INTO SchemaMigration (Version, AppliedAtUtc, AppliedBy, AppVersion)
                VALUES (@SchemaVersion, @CreatedAtUtc, @CreatedBy, @CreatedAppVersion);
                """,
                config,
                transaction);
            transaction.Commit();
        }
        catch
        {
            DeleteIfExists(path);
            DeleteIfExists(path + "-journal");
            throw;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the original error is more useful to the caller.
        }
    }
}

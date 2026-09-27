using Accession.Core.Model;
using Accession.Data;
using Accession.Data.Schema;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Tests.Data;

public sealed class DatabaseCreatorTests : IDisposable
{
    private readonly TestInventory _inventory = new();

    public void Dispose() => _inventory.Dispose();

    [Fact]
    public void Creates_all_tables()
    {
        using var scope = _inventory.Database.Open();
        var tables = scope.Connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name").ToList();

        Assert.Equal(
            ["AuditLog", "ExtensionCategory", "File", "FileCategory", "Folder", "InventoryConfig", "InventoryLock",
             "Media", "MediaExtensionSummary", "ScanError", "ScanLog", "SchemaMigration"],
            tables);
    }

    [Fact]
    public void Creates_indexes_including_partial_unique_media_index()
    {
        using var scope = _inventory.Database.Open();
        var indexes = scope.Connection.Query<(string Name, string Sql)>(
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL").ToDictionary(i => i.Name, i => i.Sql);

        Assert.Contains("UX_Media_MediaId_Active", indexes.Keys);
        Assert.Contains("WHERE IsDeleted = 0", indexes["UX_Media_MediaId_Active"]);
        Assert.Contains("UNIQUE", indexes["UX_Media_MediaId_Active"]);
        foreach (var name in new[] { "UX_Folder_Media_Path", "IX_Folder_Parent", "IX_File_Folder", "IX_File_Media_Ext",
                     "IX_File_Sha1", "IX_File_Media_Pending", "IX_File_Size", "IX_ScanLog_Media", "IX_ScanError_Media", "IX_AuditLog_Time" })
        {
            Assert.Contains(name, indexes.Keys);
        }
    }

    [Fact]
    public void Connections_use_required_pragmas()
    {
        using var scope = _inventory.Database.Open();
        var connection = scope.Connection;

        Assert.Equal(8192L, connection.ExecuteScalar<long>("PRAGMA page_size"));
        Assert.Equal("delete", connection.ExecuteScalar<string>("PRAGMA journal_mode"));
        Assert.Equal(1L, connection.ExecuteScalar<long>("PRAGMA foreign_keys"));
        Assert.Equal(2L, connection.ExecuteScalar<long>("PRAGMA synchronous")); // FULL
    }

    [Fact]
    public void Writes_config_lock_and_migration_rows()
    {
        using var scope = _inventory.Database.Open();
        var connection = scope.Connection;

        var config = connection.QuerySingle<InventoryConfig>("SELECT * FROM InventoryConfig");
        Assert.Equal(_inventory.Config.InventoryGuid, config.InventoryGuid);
        Assert.Equal(InventorySchema.CurrentVersion, config.SchemaVersion);
        Assert.Equal(_inventory.RootPath, config.RootPath);
        Assert.Equal("ACME Corporation", config.ClientName);
        Assert.Equal("ACME", config.ClientCode);
        Assert.Equal("Smith v. ACME", config.MatterName);
        Assert.Equal("2026-001", config.MatterCode);
        Assert.Equal("https://pm.example.com/matters/2026-001", config.MatterUrl);
        Assert.Equal(TestInventory.CreatedAt, config.CreatedAtUtc);
        Assert.Equal(@"CORP\jdoe", config.CreatedBy);
        Assert.Null(config.LastOpenedAtUtc);
        Assert.Equal(_inventory.DbPath, config.LastDbPath);

        Assert.Equal("2026-09-27T14:03:12.1234567Z", connection.ExecuteScalar<string>("SELECT CreatedAtUtc FROM InventoryConfig"));
        Assert.Equal(_inventory.Config.InventoryGuid.ToString("D"), connection.ExecuteScalar<string>("SELECT InventoryGuid FROM InventoryConfig"));

        var lockRow = connection.QuerySingle<(long LockId, long IsLocked)>("SELECT LockId, IsLocked FROM InventoryLock");
        Assert.Equal((1L, 0L), lockRow);

        var migration = connection.QuerySingle<(long Version, string AppliedBy)>("SELECT Version, AppliedBy FROM SchemaMigration");
        Assert.Equal((InventorySchema.CurrentVersion, @"CORP\jdoe"), ((int)migration.Version, migration.AppliedBy));
    }

    [Fact]
    public void Single_row_tables_reject_a_second_row()
    {
        using var scope = _inventory.Database.Open();

        Assert.Throws<SqliteException>(() => scope.Connection.Execute("INSERT INTO InventoryLock (LockId) VALUES (2)"));
    }

    [Fact]
    public void Refuses_to_overwrite_existing_file()
    {
        var ex = Assert.Throws<IOException>(() => DatabaseCreator.Create(_inventory.DbPath, TestInventory.NewConfig(_inventory.RootPath)));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void Failed_creation_leaves_no_file()
    {
        var path = _inventory.Temp.Combine("broken.sqlite");
        var config = TestInventory.NewConfig(_inventory.RootPath);
        config.ClientName = null!; // violates NOT NULL

        Assert.Throws<SqliteException>(() => DatabaseCreator.Create(path, config));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + "-journal"));
    }

    [Fact]
    public void Read_only_database_cannot_be_written()
    {
        var readOnly = new InventoryDatabase(_inventory.DbPath, isReadOnly: true);
        using var scope = readOnly.Open();

        Assert.Equal("ACME", scope.Connection.ExecuteScalar<string>("SELECT ClientCode FROM InventoryConfig"));
        Assert.Throws<SqliteException>(() => scope.Connection.Execute("UPDATE InventoryConfig SET ClientCode = 'X'"));
    }

    [Fact]
    public void Transaction_scope_rolls_back_unless_committed()
    {
        using (var scope = _inventory.Database.Open())
        using (scope.BeginTransaction())
        {
            scope.Connection.Execute("UPDATE InventoryConfig SET ClientCode = 'ROLLED-BACK'", transaction: scope.Transaction);
        }

        using (var scope = _inventory.Database.Open())
        {
            using (var tx = scope.BeginTransaction())
            {
                scope.Connection.Execute("UPDATE InventoryConfig SET ClientCode = 'COMMITTED'", transaction: scope.Transaction);
                tx.Commit();
            }

            Assert.Null(scope.Transaction);
        }

        using var check = _inventory.Database.Open();
        Assert.Equal("COMMITTED", check.Connection.ExecuteScalar<string>("SELECT ClientCode FROM InventoryConfig"));
    }
}

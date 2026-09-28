using Accession.Data;
using Accession.Data.Migrations;
using Accession.Data.Schema;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class MigrationTests : IDisposable
{
    private readonly TestInventory _inventory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero));

    public MigrationTests()
    {
        // The runner tests below use test-only v2 migrations, so start from a v1 file.
        SchemaDowngrade.ToV1(_inventory.DbPath);
    }

    public void Dispose() => _inventory.Dispose();

    /// <summary>Test-only v2: adds a column.</summary>
    private sealed class AddNotesColumn : IMigration
    {
        public int ToVersion => 2;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction) =>
            connection.Execute("ALTER TABLE Media ADD COLUMN TestNotes TEXT", transaction: transaction);
    }

    /// <summary>Test-only v2 that fails halfway through.</summary>
    private sealed class FailingMigration : IMigration
    {
        public int ToVersion => 2;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
            connection.Execute("ALTER TABLE Media ADD COLUMN TestNotes TEXT", transaction: transaction);
            throw new InvalidOperationException("Migration failed");
        }
    }

    private MigrationRunner V2Runner(IMigration migration) => new([migration], 2, _time);

    private long ColumnCount(string column)
    {
        using var scope = _inventory.Database.Open();
        return scope.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('Media') WHERE name = @column", new { column });
    }

    [Fact]
    public void New_inventory_is_current()
    {
        using var fresh = new TestInventory();
        var inspection = SchemaInspector.Inspect(fresh.DbPath);

        Assert.Equal(SchemaState.Current, inspection.State);
        Assert.Equal(InventorySchema.CurrentVersion, inspection.FileVersion);
    }

    [Fact]
    public void Version_1_inventory_gets_the_saved_search_tables_and_keeps_its_data()
    {
        using (var scope = _inventory.Database.Open())
        {
            scope.Connection.Execute("INSERT INTO Media (MediaId, RelativePath, Status, AddedAtUtc, AddedBy) VALUES ('M1', '\\M1\\', 'New', '2026-01-01T00:00:00.0000000Z', 'u')");
        }

        var result = new MigrationRunner(_time).Upgrade(_inventory.DbPath, @"CORP\jdoe", "0.2.0");

        Assert.Equal((1, 2), (result.FromVersion, result.ToVersion));
        Assert.Equal(SchemaState.Current, SchemaInspector.Inspect(_inventory.DbPath).State);
        using var check = _inventory.Database.Open();
        Assert.Equal(2, check.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('SavedSearch', 'SavedSearchFile')"));
        Assert.Equal("M1", check.Connection.ExecuteScalar<string>("SELECT MediaId FROM Media"));
    }

    [Fact]
    public void Older_inventory_is_upgraded_with_backup()
    {
        var runner = V2Runner(new AddNotesColumn());
        Assert.Equal(SchemaState.Older, runner.Inspect(_inventory.DbPath).State);

        var result = runner.Upgrade(_inventory.DbPath, @"CORP\jdoe", "0.2.0");

        Assert.Equal(1, result.FromVersion);
        Assert.Equal(2, result.ToVersion);
        Assert.Equal(_inventory.DbPath + ".v1.20261001083000.bak", result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(SchemaState.Current, runner.Inspect(_inventory.DbPath).State);
        Assert.Equal(1, ColumnCount("TestNotes"));

        using var scope = _inventory.Database.Open();
        var migrations = scope.Connection.Query<(long Version, string AppliedBy, string AppVersion)>(
            "SELECT Version, AppliedBy, AppVersion FROM SchemaMigration ORDER BY Version").ToList();
        Assert.Equal([(1L, @"CORP\jdoe", "0.1.0"), (2L, @"CORP\jdoe", "0.2.0")], migrations);

        // The backup is still a v1 inventory.
        Assert.Equal(1, SchemaInspector.Inspect(result.BackupPath).FileVersion);
    }

    [Fact]
    public void Failed_migration_rolls_back_and_keeps_backup()
    {
        var runner = V2Runner(new FailingMigration());

        Assert.Throws<InvalidOperationException>(() => runner.Upgrade(_inventory.DbPath, @"CORP\jdoe", "0.2.0"));

        Assert.Equal(1, SchemaInspector.Inspect(_inventory.DbPath).FileVersion);
        Assert.Equal(0, ColumnCount("TestNotes"));
        Assert.Single(Directory.GetFiles(_inventory.Temp.Path, "*.bak"));
    }

    [Fact]
    public void Newer_inventory_is_refused()
    {
        using (var scope = _inventory.Database.Open())
        {
            scope.Connection.Execute("UPDATE InventoryConfig SET SchemaVersion = 99");
        }

        var inspection = SchemaInspector.Inspect(_inventory.DbPath);
        Assert.Equal(SchemaState.Newer, inspection.State);
        Assert.Equal(99, inspection.FileVersion);

        var ex = Assert.Throws<SchemaTooNewException>(() => new MigrationRunner(_time).Upgrade(_inventory.DbPath, "u", "0.1.0"));
        Assert.Equal(99, ex.FileVersion);
        Assert.Contains("Update Accession", ex.Message);
    }

    [Fact]
    public void Random_sqlite_file_is_not_an_inventory()
    {
        var path = _inventory.Temp.Combine("other.sqlite");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            connection.Execute("CREATE TABLE Something (Id INTEGER)");
        }

        Assert.Equal(SchemaState.NotAnInventory, SchemaInspector.Inspect(path).State);
        Assert.Throws<NotAnInventoryException>(() => V2Runner(new AddNotesColumn()).Upgrade(path, "u", "0.2.0"));
    }

    [Fact]
    public void Non_sqlite_file_is_not_an_inventory()
    {
        var path = _inventory.Temp.Combine("notes.sqlite");
        File.WriteAllText(path, "This is a text file pretending to be a database, long enough to fill a header page.");

        Assert.Equal(SchemaState.NotAnInventory, SchemaInspector.Inspect(path).State);
    }

    [Fact]
    public void Inspection_does_not_modify_the_file()
    {
        var before = File.GetLastWriteTimeUtc(_inventory.DbPath);
        var bytes = File.ReadAllBytes(_inventory.DbPath);

        SchemaInspector.Inspect(_inventory.DbPath);

        Assert.Equal(bytes, File.ReadAllBytes(_inventory.DbPath));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_inventory.DbPath));
    }

    [Fact]
    public void Migrations_must_be_consecutive()
    {
        Assert.Throws<InvalidOperationException>(() => new MigrationRunner([new SkipToThree()], 3, _time));
    }

    private sealed class SkipToThree : IMigration
    {
        public int ToVersion => 3;

        public void Apply(SqliteConnection connection, SqliteTransaction transaction)
        {
        }
    }
}

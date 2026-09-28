using Accession.Data;
using Accession.Data.Migrations;
using Accession.Tests.TestSupport;
using Dapper;

namespace Accession.Tests.Data;

/// <summary>
/// An inventory left with a hot journal (the app was killed in the middle of a write) must still open: SQLite rolls the
/// journal back on the next read, which a read-only connection cannot do.
/// </summary>
public sealed class HotJournalTests : IDisposable
{
    private readonly TestInventory _inventory = new();

    public void Dispose() => _inventory.Dispose();

    /// <summary>Copies the inventory and its journal in the middle of a write: the copy has a hot journal.</summary>
    private string CopyWithHotJournal()
    {
        var copy = _inventory.Temp.Combine("Crashed.sqlite");
        using var connection = SqliteConnectionFactory.Open(_inventory.DbPath);
        connection.Execute("PRAGMA cache_size = 1");
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < 2_000; i++)
        {
            connection.Execute("INSERT INTO AuditLog (OccurredAtUtc, UserName, MachineName, Action) VALUES ('2026-01-01', 'u', 'm', 'Test')", transaction: transaction);
        }

        File.Copy(_inventory.DbPath, copy);
        File.Copy(_inventory.DbPath + "-journal", copy + "-journal");
        transaction.Rollback();
        return copy;
    }

    [Fact]
    public void A_hot_journal_is_recovered_and_the_inventory_inspected()
    {
        var copy = CopyWithHotJournal();
        Assert.True(new FileInfo(copy + "-journal").Length > 0);

        var inspection = SchemaInspector.Inspect(copy);

        Assert.Equal(SchemaState.Current, inspection.State);
        Assert.False(File.Exists(copy + "-journal")); // rolled back
    }

    [Fact]
    public void Recovery_keeps_what_was_committed_and_drops_the_interrupted_change()
    {
        long before;
        using (var connection = SqliteConnectionFactory.Open(_inventory.DbPath, readOnly: true))
        {
            before = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM AuditLog");
        }

        var copy = CopyWithHotJournal();

        using var readOnly = SqliteConnectionFactory.Open(copy, readOnly: true);
        Assert.Equal(before, readOnly.ExecuteScalar<long>("SELECT COUNT(*) FROM AuditLog")); // none of the 2,000 interrupted rows
        Assert.Equal(1, readOnly.ExecuteScalar<long>("SELECT COUNT(*) FROM InventoryConfig"));
        Assert.Equal("ok", readOnly.ExecuteScalar<string>("PRAGMA integrity_check"));
    }

    [Fact]
    public void A_file_that_cannot_be_written_explains_what_to_do()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the read-only attribute is what stops the write; elsewhere permissions (and root) differ
        }

        var copy = CopyWithHotJournal();
        File.SetAttributes(copy, FileAttributes.ReadOnly);
        try
        {
            var error = Assert.Throws<InventoryNeedsRecoveryException>(() => SchemaInspector.Inspect(copy));
            Assert.Contains("was not closed properly", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(copy, FileAttributes.Normal);
        }
    }
}

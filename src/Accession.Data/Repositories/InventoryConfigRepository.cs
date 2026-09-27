using Accession.Core.Model;
using Dapper;

namespace Accession.Data.Repositories;

public sealed class InventoryConfigRepository(DbScope scope) : RepositoryBase(scope)
{
    public InventoryConfig Get() =>
        Connection.QuerySingle<InventoryConfig>("SELECT * FROM InventoryConfig WHERE ConfigId = 1", transaction: Transaction);

    public void UpdateMatter(MatterInfo matter)
    {
        ArgumentNullException.ThrowIfNull(matter);
        Connection.Execute(
            """
            UPDATE InventoryConfig
            SET ClientName = @ClientName, ClientCode = @ClientCode, MatterName = @MatterName,
                MatterCode = @MatterCode, Description = @Description, MatterUrl = @MatterUrl
            WHERE ConfigId = 1
            """,
            matter,
            Transaction);
    }

    public void UpdateRootPath(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        Connection.Execute("UPDATE InventoryConfig SET RootPath = @rootPath WHERE ConfigId = 1", new { rootPath }, Transaction);
    }

    public void UpdateLastOpened(DateTimeOffset openedAt, string openedBy, string dbPath) =>
        Connection.Execute(
            "UPDATE InventoryConfig SET LastOpenedAtUtc = @openedAt, LastOpenedBy = @openedBy, LastDbPath = @dbPath WHERE ConfigId = 1",
            new { openedAt, openedBy, dbPath },
            Transaction);
}

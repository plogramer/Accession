using Microsoft.Data.Sqlite;

namespace Accession.Data.Migrations;

/// <summary>Upgrades an inventory from <c>ToVersion - 1</c> to <see cref="ToVersion"/>.</summary>
public interface IMigration
{
    int ToVersion { get; }

    /// <summary>Applies the change inside the runner's transaction. Must not commit.</summary>
    void Apply(SqliteConnection connection, SqliteTransaction transaction);
}

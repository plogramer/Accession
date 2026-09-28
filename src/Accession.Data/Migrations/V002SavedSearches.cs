using Accession.Data.Schema;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Migrations;

/// <summary>Schema v2: the SavedSearch and SavedSearchFile tables.</summary>
internal sealed class V002SavedSearches : IMigration
{
    public const string Script = "V002_SavedSearches.sql";

    public int ToVersion => 2;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction) =>
        connection.Execute(InventorySchema.LoadScript(Script), transaction: transaction);
}

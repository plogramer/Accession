using Accession.Data.Schema;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Migrations;

/// <summary>
/// Schema v3: SavedSearch.CreatedOnMachine and ModifiedOnMachine. Existing saved searches get the computer from the
/// audit log entry that recorded their creation (and their last change).
/// </summary>
internal sealed class V003SavedSearchMachine : IMigration
{
    public const string Script = "V003_SavedSearchMachine.sql";

    public int ToVersion => 3;

    public void Apply(SqliteConnection connection, SqliteTransaction transaction)
    {
        connection.Execute(InventorySchema.LoadScript(Script), transaction: transaction);
        connection.Execute(
            """
            UPDATE SavedSearch SET
                CreatedOnMachine = (SELECT a.MachineName FROM AuditLog a
                    WHERE a.Action = 'SavedSearchCreated' AND json_extract(a.Details, '$.savedSearchId') = SavedSearch.SavedSearchId
                    ORDER BY a.AuditId LIMIT 1),
                ModifiedOnMachine = (SELECT a.MachineName FROM AuditLog a
                    WHERE a.Action IN ('SavedSearchCreated', 'SavedSearchChanged', 'SavedSearchFilesAdded', 'SavedSearchFilesRemoved')
                      AND json_extract(a.Details, '$.savedSearchId') = SavedSearch.SavedSearchId
                    ORDER BY a.AuditId DESC LIMIT 1)
            """,
            transaction: transaction);
    }
}

using Accession.Data.Schema;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Migrations;

public enum SchemaState
{
    /// <summary>Schema matches this application version.</summary>
    Current,

    /// <summary>Created by an older version; can be upgraded.</summary>
    Older,

    /// <summary>Created by a newer version; this application must be updated to open it.</summary>
    Newer,

    /// <summary>Not a SQLite file, or a SQLite file that is not an Accession inventory.</summary>
    NotAnInventory,
}

public sealed record SchemaInspection(SchemaState State, int? FileVersion, int AppVersion);

/// <summary>Reads the schema version of an inventory file without changing it (requirement INV-11).</summary>
public static class SchemaInspector
{
    // SQLITE_NOTADB: the file is not a database.
    private const int SqliteNotADatabase = 26;

    public static SchemaInspection Inspect(string path) => Inspect(path, InventorySchema.CurrentVersion);

    internal static SchemaInspection Inspect(string path, int appVersion)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Inventory file not found.", path);
        }

        try
        {
            using var connection = SqliteConnectionFactory.Open(path, readOnly: true);
            var hasConfig = connection.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('InventoryConfig', 'SchemaMigration')") == 2;
            if (!hasConfig)
            {
                return new SchemaInspection(SchemaState.NotAnInventory, null, appVersion);
            }

            var fileVersion = connection.ExecuteScalar<int?>("SELECT SchemaVersion FROM InventoryConfig WHERE ConfigId = 1");
            if (fileVersion is null)
            {
                return new SchemaInspection(SchemaState.NotAnInventory, null, appVersion);
            }

            var state = fileVersion.Value == appVersion ? SchemaState.Current
                : fileVersion.Value < appVersion ? SchemaState.Older
                : SchemaState.Newer;
            return new SchemaInspection(state, fileVersion, appVersion);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteNotADatabase)
        {
            return new SchemaInspection(SchemaState.NotAnInventory, null, appVersion);
        }
    }
}

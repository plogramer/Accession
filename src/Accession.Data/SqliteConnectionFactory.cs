using Microsoft.Data.Sqlite;

namespace Accession.Data;

/// <summary>Opens connections to inventory files with the settings required by NFR-04.</summary>
public static class SqliteConnectionFactory
{
    /// <summary>Seconds to wait on a locked database before failing.</summary>
    public const int BusyTimeoutSeconds = 30;

    public static SqliteConnection Open(string path, bool readOnly = false) =>
        Open(path, readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite);

    internal static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        DapperSetup.EnsureRegistered();

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            // No pooling: pooled connections keep the file open, which blocks backups/moves and
            // holds SMB handles on network shares.
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds,
        };

        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {BusyTimeoutSeconds * 1000};";
            if (mode != SqliteOpenMode.ReadOnly)
            {
                // WAL is not safe over SMB; use a rollback journal and full sync (NFR-04).
                command.CommandText += " PRAGMA journal_mode = DELETE; PRAGMA synchronous = FULL;";
            }

            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

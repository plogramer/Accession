using Microsoft.Data.Sqlite;

namespace Accession.Data;

/// <summary>Opens connections to inventory files with the settings required by NFR-04.</summary>
public static class SqliteConnectionFactory
{
    /// <summary>Seconds to wait on a locked database before failing.</summary>
    public const int BusyTimeoutSeconds = 30;

    // SQLITE_READONLY: a read-only connection was asked to write (here: to roll back a hot journal).
    private const int SqliteReadOnly = 8;

    public static SqliteConnection Open(string path, bool readOnly = false) =>
        Open(path, readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite);

    internal static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        if (mode != SqliteOpenMode.ReadOnly)
        {
            return OpenCore(path, mode);
        }

        var connection = OpenCore(path, mode);
        try
        {
            Probe(connection);
            return connection;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteReadOnly && File.Exists(path + "-journal"))
        {
            // A hot journal: the app (or the computer) stopped in the middle of a write. SQLite must roll it back before
            // the file can be read, which needs write access. Rolling back only restores the last committed state.
            connection.Dispose();
            RecoverHotJournal(path);
            connection = OpenCore(path, mode);
            Probe(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Opens the file read-write once so SQLite rolls back an interrupted write (hot journal).</summary>
    public static void RecoverHotJournal(string path)
    {
        try
        {
            using var connection = OpenCore(path, SqliteOpenMode.ReadWrite);
            Probe(connection);
        }
        catch (Exception ex) when (ex is SqliteException or UnauthorizedAccessException or IOException)
        {
            throw new InventoryNeedsRecoveryException(
                "The inventory was not closed properly: a change was interrupted (for example the app or the computer stopped " +
                $"during a scan). It must be opened once by someone who can write to the file so the interrupted change can be " +
                $"undone.\n\n{path}", ex);
        }
    }

    /// <summary>Reads the schema, which makes SQLite check for (and roll back) a hot journal.</summary>
    private static void Probe(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
        command.ExecuteScalar();
    }

    private static SqliteConnection OpenCore(string path, SqliteOpenMode mode)
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

/// <summary>
/// The inventory has an interrupted change (hot journal) that must be undone before it can be read, and the file cannot
/// be written from here (read-only share or no permission).
/// </summary>
public sealed class InventoryNeedsRecoveryException(string message, Exception inner) : InvalidOperationException(message, inner);

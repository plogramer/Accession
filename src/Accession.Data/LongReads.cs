using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Accession.Data;

/// <summary>
/// Long background reads (the Dashboard's duplicates and years: tens of seconds on millions of files) that give way to
/// everything else. Inventories use a rollback journal (WAL is not safe on network shares, NFR-04): a write must wait
/// until every reader has finished, and while it waits no new read may start. A long report query would therefore
/// freeze the app behind any small write (adding media, queueing a scan, an audit entry). So every write first interrupts
/// the long reads on that inventory (SQLite error 9: <see cref="WriteStarting"/> at the start of transactions, audit
/// entries, scan batches and the lock heartbeat); they are run again later.
/// </summary>
public static class LongReads
{
    // SQLITE_INTERRUPT: the query was stopped by sqlite3_interrupt.
    public const int InterruptedErrorCode = 9;

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<long, Entry>> Active = new(StringComparer.OrdinalIgnoreCase);
    private static long _next;

    /// <summary>Marks <paramref name="connection"/> as running a long read until disposed.</summary>
    public static IDisposable Enter(string databasePath, SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var id = Interlocked.Increment(ref _next);
        var reads = Active.GetOrAdd(Key(databasePath), _ => new ConcurrentDictionary<long, Entry>());
        var entry = new Entry(connection);
        reads[id] = entry;
        return new Lease(() =>
        {
            reads.TryRemove(id, out _);
            entry.Release();
        });
    }

    /// <summary>Stops the long reads running on this inventory. Returns how many were interrupted.</summary>
    public static int Interrupt(string databasePath)
    {
        if (!Active.TryGetValue(Key(databasePath), out var reads))
        {
            return 0;
        }

        return reads.Values.Count(entry => entry.Interrupt());
    }

    /// <summary>True when <paramref name="ex"/> is a query stopped by <see cref="Interrupt"/>.</summary>
    public static bool IsInterrupted(Exception ex) => ex is SqliteException { SqliteErrorCode: InterruptedErrorCode };

    /// <summary>A write is about to start on this inventory: long reads give way first.</summary>
    public static void WriteStarting(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Active.IsEmpty && connection.DataSource is { Length: > 0 } path)
        {
            Interrupt(path);
        }
    }

    private static string Key(string path) => Path.GetFullPath(path);

    /// <summary>A registered connection; interrupting and releasing are serialized, so a closed connection is never touched.</summary>
    private sealed class Entry(SqliteConnection connection)
    {
        private readonly Lock _gate = new();
        private bool _released;

        public bool Interrupt()
        {
            lock (_gate)
            {
                if (_released || connection.Handle is not { } handle)
                {
                    return false;
                }

                raw.sqlite3_interrupt(handle);
                return true;
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                _released = true;
            }
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

using System.Threading.Channels;
using Accession.Core.Time;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Scanning;

/// <summary>
/// The single database writer for a scan (SCN-22). Producers send <see cref="WriteCommand"/>s through a bounded
/// channel (back-pressure); one task applies them on one connection and commits every <c>batchSize</c> rows or
/// every 2 seconds, whichever comes first.
/// </summary>
public sealed class ScanDbWriter : IAsyncDisposable
{
    public static readonly TimeSpan MaxBatchAge = TimeSpan.FromSeconds(2);

    private readonly Channel<object> _channel;
    private readonly int _batchSize;
    private readonly TimeProvider _timeProvider;
    private readonly Task _loop;
    private long _rowsWritten;
    private long _commits;

    public ScanDbWriter(InventoryDatabase database, int batchSize, TimeProvider timeProvider, int capacity = 2048)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        _batchSize = batchSize;
        _timeProvider = timeProvider;
        _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _loop = Task.Run(() => RunAsync(database.Path));
    }

    /// <summary>Completes when the writer stopped; faulted if a database error occurred.</summary>
    public Task Completion => _loop;

    public long RowsWritten => Interlocked.Read(ref _rowsWritten);

    public long Commits => Interlocked.Read(ref _commits);

    /// <summary>Queues a command; waits while the queue is full.</summary>
    public ValueTask WriteAsync(WriteCommand command, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(command, cancellationToken);

    /// <summary>Commits everything queued before this call.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(flushed, cancellationToken).ConfigureAwait(false);
        await flushed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops accepting commands, commits what is queued and waits for the writer to finish.</summary>
    public async Task CompleteAsync()
    {
        _channel.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // Errors are observed through Completion / CompleteAsync.
        }
    }

    private async Task RunAsync(string path)
    {
        using var connection = SqliteConnectionFactory.Open(path);
        using var statements = new Statements(connection);
        SqliteTransaction? transaction = null;
        var rows = 0;
        var batchStarted = 0L;
        var reader = _channel.Reader;

        void Commit()
        {
            if (transaction is null)
            {
                return;
            }

            transaction.Commit();
            transaction.Dispose();
            transaction = null;
            Interlocked.Add(ref _rowsWritten, rows);
            Interlocked.Increment(ref _commits);
            rows = 0;
        }

        try
        {
            while (true)
            {
                if (transaction is not null)
                {
                    var remaining = MaxBatchAge - _timeProvider.GetElapsedTime(batchStarted);
                    if (remaining <= TimeSpan.Zero)
                    {
                        Commit();
                        continue;
                    }

                    var waitForData = reader.WaitToReadAsync().AsTask();
                    using var delayCancel = new CancellationTokenSource();
                    var delay = Task.Delay(remaining, _timeProvider, delayCancel.Token);
                    if (await Task.WhenAny(waitForData, delay).ConfigureAwait(false) == delay)
                    {
                        Commit();
                        continue;
                    }

                    delayCancel.Cancel();
                    if (!await waitForData.ConfigureAwait(false))
                    {
                        break;
                    }
                }
                else if (!await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    break;
                }

                while (reader.TryRead(out var item))
                {
                    if (item is TaskCompletionSource flush)
                    {
                        Commit();
                        flush.TrySetResult();
                        continue;
                    }

                    if (transaction is null)
                    {
                        transaction = connection.BeginTransaction(deferred: false);
                        statements.Transaction = transaction;
                        batchStarted = _timeProvider.GetTimestamp();
                    }

                    rows += statements.Apply((WriteCommand)item);
                    if (rows >= _batchSize)
                    {
                        Commit();
                    }
                }
            }

            Commit();
        }
        catch (Exception ex)
        {
            transaction?.Dispose(); // rolls back the uncommitted batch
            _channel.Writer.TryComplete(ex);
            while (reader.TryRead(out var pending))
            {
                (pending as TaskCompletionSource)?.TrySetException(ex);
            }

            throw;
        }
    }

    /// <summary>Prepared statements reused for every row.</summary>
    private sealed class Statements : IDisposable
    {
        private readonly SqliteCommand _insertFolder;
        private readonly SqliteCommand _insertFile;
        private readonly SqliteCommand _markEnumerated;
        private readonly SqliteCommand _updateHash;
        private readonly SqliteCommand _insertError;

        public Statements(SqliteConnection connection)
        {
            _insertFolder = Create(connection,
                """
                INSERT INTO Folder (FolderId, MediaKey, ParentFolderId, Name, RelativePath, CreatedUtc, ModifiedUtc, AccessedUtc, IsReparsePoint, IsEnumerated)
                VALUES ($id, $media, $parent, $name, $path, $created, $modified, $accessed, $reparse, $enumerated)
                """,
                "$id", "$media", "$parent", "$name", "$path", "$created", "$modified", "$accessed", "$reparse", "$enumerated");
            _insertFile = Create(connection,
                """
                INSERT INTO File (FileId, MediaKey, FolderId, Name, Extension, SizeBytes, CreatedUtc, ModifiedUtc, AccessedUtc, HashStatus)
                VALUES ($id, $media, $folder, $name, $ext, $size, $created, $modified, $accessed, 0)
                """,
                "$id", "$media", "$folder", "$name", "$ext", "$size", "$created", "$modified", "$accessed");
            _markEnumerated = Create(connection, "UPDATE Folder SET IsEnumerated = 1 WHERE FolderId = $id", "$id");
            _updateHash = Create(connection,
                "UPDATE File SET Sha1 = $sha1, HashStatus = $status, HashedAtUtc = $at WHERE FileId = $id",
                "$sha1", "$status", "$at", "$id");
            _insertError = Create(connection,
                """
                INSERT INTO ScanError (MediaKey, ScanId, RelativePath, ItemType, ErrorType, Severity, ErrorCode, Message, OccurredAtUtc)
                VALUES ($media, $scan, $path, $item, $type, $severity, $code, $message, $at)
                """,
                "$media", "$scan", "$path", "$item", "$type", "$severity", "$code", "$message", "$at");
        }

        public SqliteTransaction? Transaction
        {
            set
            {
                _insertFolder.Transaction = value;
                _insertFile.Transaction = value;
                _markEnumerated.Transaction = value;
                _updateHash.Transaction = value;
                _insertError.Transaction = value;
            }
        }

        /// <summary>Applies a command and returns the number of rows written.</summary>
        public int Apply(WriteCommand command)
        {
            switch (command)
            {
                case InsertFolderCommand c:
                    InsertFolder(c.Folder);
                    return 1;

                case FolderListingCommand c:
                    foreach (var folder in c.ChildFolders)
                    {
                        InsertFolder(folder);
                    }

                    foreach (var file in c.Files)
                    {
                        InsertFile(file);
                    }

                    Run(_markEnumerated, c.FolderId);
                    return c.ChildFolders.Count + c.Files.Count + 1;

                case HashResultCommand c:
                    Run(_updateHash, c.Sha1, (int)c.Status, Text(c.HashedAtUtc), c.FileId);
                    return 1;

                case ErrorCommand c:
                    var e = c.Error;
                    Run(_insertError, e.MediaKey, e.ScanId, e.RelativePath, e.ItemType.ToString(), e.ErrorType.ToString(),
                        e.Severity.ToString(), e.ErrorCode, e.Message, UtcTimestamp.ToText(e.OccurredAtUtc));
                    return 1;

                default:
                    throw new ArgumentException($"Unknown write command {command.GetType().Name}.", nameof(command));
            }
        }

        public void Dispose()
        {
            _insertFolder.Dispose();
            _insertFile.Dispose();
            _markEnumerated.Dispose();
            _updateHash.Dispose();
            _insertError.Dispose();
        }

        private void InsertFolder(FolderRow f) =>
            Run(_insertFolder, f.FolderId, f.MediaKey, f.ParentFolderId, f.Name, f.RelativePath,
                Text(f.CreatedUtc), Text(f.ModifiedUtc), Text(f.AccessedUtc), f.IsReparsePoint ? 1 : 0, f.IsEnumerated ? 1 : 0);

        private void InsertFile(FileRow f) =>
            Run(_insertFile, f.FileId, f.MediaKey, f.FolderId, f.Name, f.Extension, f.SizeBytes,
                Text(f.CreatedUtc), Text(f.ModifiedUtc), Text(f.AccessedUtc));

        private static string? Text(DateTimeOffset? value) => value is { } v ? UtcTimestamp.ToText(v) : null;

        private static SqliteCommand Create(SqliteConnection connection, string sql, params string[] parameterNames)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var name in parameterNames)
            {
                command.Parameters.Add(new SqliteParameter { ParameterName = name });
            }

            command.Prepare();
            return command;
        }

        private static void Run(SqliteCommand command, params object?[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
            }

            command.ExecuteNonQuery();
        }
    }
}

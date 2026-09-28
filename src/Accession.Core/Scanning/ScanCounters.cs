using System.Collections.Concurrent;

namespace Accession.Core.Scanning;

/// <summary>Thread-safe counters updated by the enumeration and hashing workers.</summary>
public sealed class ScanCounters
{
    private long _foldersFound;
    private long _filesFound;
    private long _bytesFound;
    private long _filesHashed;
    private long _bytesHashed;
    private long _errors;
    private volatile string? _currentPath;
    private volatile bool _enumerationDone;

    public long FoldersFound => Interlocked.Read(ref _foldersFound);
    public long FilesFound => Interlocked.Read(ref _filesFound);
    public long BytesFound => Interlocked.Read(ref _bytesFound);
    public long FilesHashed => Interlocked.Read(ref _filesHashed);
    public long BytesHashed => Interlocked.Read(ref _bytesHashed);
    public long Errors => Interlocked.Read(ref _errors);

    private readonly ConcurrentDictionary<long, FileInProgress> _hashing = new();
    private long _started;

    /// <summary>
    /// What is being worked on: while files are being hashed, the one that has been hashing longest (a large file shows
    /// here until it is done, not whichever small file another thread started last); otherwise the item most recently
    /// started (e.g. the folder being listed).
    /// </summary>
    public string? CurrentPath
    {
        get => LongestHashing()?.Path ?? _currentPath;
        set => _currentPath = value;
    }

    /// <summary>The file that has been hashing longest, with its size and the bytes read so far; null when none.</summary>
    public FileInProgress? LongestHashing()
    {
        FileInProgress? longest = null;
        foreach (var file in _hashing.Values)
        {
            if (longest is null || file.Order < longest.Order)
            {
                longest = file;
            }
        }

        return longest;
    }

    /// <summary>A file starts hashing. Call <see cref="EndHashing"/> when its hash step is done.</summary>
    public FileInProgress BeginHashing(long fileId, string path, long size)
    {
        var file = new FileInProgress(path, size, Interlocked.Increment(ref _started));
        _hashing[fileId] = file;
        return file;
    }

    /// <summary>Bytes read from a file being hashed: the totals (speed, percentage) move while large files are read.</summary>
    public void AddHashedBytes(FileInProgress file, long bytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Add(bytes);
        Interlocked.Add(ref _bytesHashed, bytes);
    }

    /// <summary>A retry reads the file again from the start: what was read before no longer counts.</summary>
    public void RestartHashing(FileInProgress file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Interlocked.Add(ref _bytesHashed, -file.Restart());
    }

    /// <summary>The scan stopped while the file was being hashed (cancel, pause for shutdown): it does not count.</summary>
    public void AbandonHashing(long fileId, FileInProgress file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _hashing.TryRemove(fileId, out _);
        Interlocked.Add(ref _bytesHashed, -file.Restart());
    }

    /// <summary>
    /// The file's hash step finished (successfully or not). Its bytes count as its size in total, whatever was reported
    /// while reading (a retry re-reads, a failure stops early).
    /// </summary>
    public void EndHashing(long fileId, FileInProgress file, long size)
    {
        ArgumentNullException.ThrowIfNull(file);
        _hashing.TryRemove(fileId, out _);
        Interlocked.Increment(ref _filesHashed);
        Interlocked.Add(ref _bytesHashed, size - file.BytesRead);
    }

    /// <summary>True once enumeration finished, so totals are final and an ETA can be given.</summary>
    public bool EnumerationDone
    {
        get => _enumerationDone;
        set => _enumerationDone = value;
    }

    public void AddFolders(long count) => Interlocked.Add(ref _foldersFound, count);

    public void AddFiles(long count, long bytes)
    {
        Interlocked.Add(ref _filesFound, count);
        Interlocked.Add(ref _bytesFound, bytes);
    }

    /// <summary>Counts a file whose hash step finished (successfully or not).</summary>
    public void AddHashed(long bytes)
    {
        Interlocked.Increment(ref _filesHashed);
        Interlocked.Add(ref _bytesHashed, bytes);
    }

    public void AddError() => Interlocked.Increment(ref _errors);
}

/// <summary>A file being hashed: path, size and bytes read so far (thread-safe).</summary>
public sealed class FileInProgress(string path, long size, long order)
{
    private long _bytesRead;

    public string Path { get; } = path;

    public long Size { get; } = size;

    /// <summary>Start order: lower started earlier.</summary>
    public long Order { get; } = order;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    internal void Add(long bytes) => Interlocked.Add(ref _bytesRead, bytes);

    /// <summary>Starts counting from zero again; returns what had been read.</summary>
    internal long Restart() => Interlocked.Exchange(ref _bytesRead, 0);
}

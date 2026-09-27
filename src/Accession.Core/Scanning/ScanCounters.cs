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

    /// <summary>Relative path of the item most recently started.</summary>
    public string? CurrentPath
    {
        get => _currentPath;
        set => _currentPath = value;
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

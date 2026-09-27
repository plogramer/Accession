using Dapper;

namespace Accession.Data.Scanning;

/// <summary>
/// Hands out Folder/File IDs without a database round trip. Safe because only the lock holder writes
/// to the inventory and only one scan runs at a time.
/// </summary>
public sealed class ScanIdAllocator
{
    private long _lastFolderId;
    private long _lastFileId;

    private ScanIdAllocator(long lastFolderId, long lastFileId)
    {
        _lastFolderId = lastFolderId;
        _lastFileId = lastFileId;
    }

    public static ScanIdAllocator Create(DbScope scope)
    {
        var folder = scope.Connection.ExecuteScalar<long?>("SELECT MAX(FolderId) FROM Folder", transaction: scope.Transaction) ?? 0;
        var file = scope.Connection.ExecuteScalar<long?>("SELECT MAX(FileId) FROM File", transaction: scope.Transaction) ?? 0;
        return new ScanIdAllocator(folder, file) { InitialLastFileId = file };
    }

    /// <summary>Highest FileId that existed when the allocator was created (files above it are new in this run).</summary>
    public long InitialLastFileId { get; private init; }

    public long NextFolderId() => Interlocked.Increment(ref _lastFolderId);

    public long NextFileId() => Interlocked.Increment(ref _lastFileId);
}

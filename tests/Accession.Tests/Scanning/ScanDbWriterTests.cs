using System.Diagnostics;
using Accession.Core.Model;
using Accession.Data.Repositories;
using Accession.Data.Scanning;
using Accession.Tests.TestSupport;
using Dapper;

namespace Accession.Tests.Scanning;

public sealed class ScanDbWriterTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly TestInventory _inventory = new();
    private readonly long _mediaKey;

    public ScanDbWriterTests()
    {
        using var scope = _inventory.Database.Open();
        _mediaKey = new MediaRepository(scope).Insert("M1", @"\M1\", T0, "u");
    }

    public void Dispose() => _inventory.Dispose();

    private ScanIdAllocator Ids()
    {
        using var scope = _inventory.Database.Open();
        return ScanIdAllocator.Create(scope);
    }

    private FolderRow Folder(long id, long? parent, string path, bool enumerated = false) =>
        new(id, _mediaKey, parent, path.TrimEnd('\\').Split('\\')[^1], path, T0, T0, T0, false, enumerated);

    private FileRow File(long id, long folder, string name, long size = 10) =>
        new(id, _mediaKey, folder, name, Path.GetExtension(name).TrimStart('.'), size, T0, T0.AddDays(1), T0);

    private long Count(string sql)
    {
        using var scope = _inventory.Database.Open();
        return scope.Connection.ExecuteScalar<long>(sql);
    }

    [Fact]
    public async Task Writes_folders_files_hashes_and_errors()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        var sub = ids.NextFolderId();
        var fileId = ids.NextFileId();
        await using (var writer = new ScanDbWriter(_inventory.Database, 100, TimeProvider.System))
        {
            await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));
            await writer.WriteAsync(new FolderListingCommand(root, [Folder(sub, root, @"\M1\sub\")], [File(fileId, root, "a.msg", 123)]));
            await writer.WriteAsync(new HashResultCommand(fileId, new string('a', 40), HashStatus.Hashed, T0));
            await writer.WriteAsync(new ErrorCommand(new ScanErrorEntry
            {
                MediaKey = _mediaKey, RelativePath = @"\M1\sub\", ItemType = ScanItemType.Folder,
                ErrorType = ScanErrorType.AccessDenied, ErrorCode = 5, Message = "denied", OccurredAtUtc = T0,
            }));
            await writer.CompleteAsync();
            Assert.Equal(6, writer.RowsWritten);
        }

        using var scope = _inventory.Database.Open();
        var folders = scope.Connection.Query<(long FolderId, long? ParentFolderId, string RelativePath, long IsEnumerated)>(
            "SELECT FolderId, ParentFolderId, RelativePath, IsEnumerated FROM Folder ORDER BY FolderId").ToList();
        Assert.Equal([(root, (long?)null, @"\M1\", 1L), (sub, root, @"\M1\sub\", 0L)], folders);
        var file = scope.Connection.QuerySingle<(string Name, string Extension, long SizeBytes, string ModifiedUtc, string Sha1, long HashStatus)>(
            "SELECT Name, Extension, SizeBytes, ModifiedUtc, Sha1, HashStatus FROM File");
        Assert.Equal(("a.msg", "msg", 123L, "2026-09-28T09:00:00.0000000Z", new string('a', 40), 1L), file);
        Assert.Equal(1, new ScanErrorRepository(scope).CountByMedia(_mediaKey));
    }

    [Fact]
    public async Task Allocator_continues_after_existing_ids()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        await using (var writer = new ScanDbWriter(_inventory.Database, 100, TimeProvider.System))
        {
            await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));
            await writer.WriteAsync(new FolderListingCommand(root, [], [File(ids.NextFileId(), root, "a.txt")]));
            await writer.CompleteAsync();
        }

        var next = Ids();
        Assert.Equal(root + 1, next.NextFolderId());
        Assert.Equal(1, next.InitialLastFileId);
        Assert.Equal(2, next.NextFileId());
    }

    [Fact]
    public async Task Commits_in_batches_of_batch_size()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        await using var writer = new ScanDbWriter(_inventory.Database, batchSize: 10, TimeProvider.System);
        await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));
        for (var i = 0; i < 5; i++)
        {
            var files = Enumerable.Range(0, 5).Select(_ => File(ids.NextFileId(), root, "f.txt")).ToList();
            await writer.WriteAsync(new FolderListingCommand(root, [], files));
        }

        await writer.CompleteAsync();

        Assert.Equal(1 + (5 * 6), writer.RowsWritten);
        Assert.True(writer.Commits >= 3, $"Expected several commits, got {writer.Commits}.");
    }

    [Fact]
    public async Task Flush_commits_so_other_connections_see_the_rows()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        await using var writer = new ScanDbWriter(_inventory.Database, batchSize: 1_000_000, TimeProvider.System);
        await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, Count("SELECT COUNT(*) FROM Folder"));
        await writer.CompleteAsync();
    }

    [Fact]
    public async Task Idle_batch_is_committed_soon_so_the_write_lock_is_released()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        await using var writer = new ScanDbWriter(_inventory.Database, batchSize: 1_000_000, TimeProvider.System);
        await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Count("SELECT COUNT(*) FROM Folder") == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, Count("SELECT COUNT(*) FROM Folder"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"Committed after {watch.Elapsed.TotalMilliseconds:0} ms."); // not the 2 s batch age
        await writer.CompleteAsync();
    }

    [Fact]
    public async Task Failed_batch_is_rolled_back_and_earlier_listings_stay_complete()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        var good = ids.NextFolderId();
        var bad = ids.NextFolderId();
        var writer = new ScanDbWriter(_inventory.Database, batchSize: 1_000_000, TimeProvider.System);
        await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));
        await writer.WriteAsync(new FolderListingCommand(root, [Folder(good, root, @"\M1\good\"), Folder(bad, root, @"\M1\bad\")], []));
        await writer.WriteAsync(new FolderListingCommand(good, [], [File(ids.NextFileId(), good, "a.txt")]));
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Listing of "bad" contains a file whose folder does not exist: foreign key failure mid-listing.
        await writer.WriteAsync(new FolderListingCommand(bad, [], [File(ids.NextFileId(), bad, "ok.txt"), File(ids.NextFileId(), 9999, "broken.txt")]));

        await Assert.ThrowsAnyAsync<Exception>(writer.CompleteAsync);
        Assert.True(writer.Completion.IsFaulted);
        Assert.Equal(1, Count("SELECT COUNT(*) FROM File"));
        Assert.Equal(0, Count($"SELECT IsEnumerated FROM Folder WHERE FolderId = {bad}"));
        Assert.Equal(1, Count($"SELECT IsEnumerated FROM Folder WHERE FolderId = {good}"));
        Assert.Equal(0, Count($"SELECT COUNT(*) FROM File WHERE FolderId = {bad}"));
    }

    [Fact]
    public async Task Writes_200k_file_rows()
    {
        var ids = Ids();
        var root = ids.NextFolderId();
        var stopwatch = Stopwatch.StartNew();
        await using (var writer = new ScanDbWriter(_inventory.Database, batchSize: 10_000, TimeProvider.System))
        {
            await writer.WriteAsync(new InsertFolderCommand(Folder(root, null, @"\M1\")));
            for (var folder = 0; folder < 200; folder++)
            {
                var folderId = ids.NextFolderId();
                var files = Enumerable.Range(0, 1000).Select(i => File(ids.NextFileId(), folderId, $"file{i}.txt")).ToList();
                await writer.WriteAsync(new FolderListingCommand(root, [Folder(folderId, root, $@"\M1\f{folder}\", enumerated: true)], files));
            }

            await writer.CompleteAsync();
        }

        stopwatch.Stop();
        TestContext.Current.SendDiagnosticMessage($"200k file rows in {stopwatch.Elapsed.TotalSeconds:0.0}s");
        Assert.Equal(200_000, Count("SELECT COUNT(*) FROM File"));
    }
}

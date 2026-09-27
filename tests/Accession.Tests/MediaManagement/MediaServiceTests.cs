using Accession.Core.Model;
using Accession.Data.Audit;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;

namespace Accession.Tests.MediaManagement;

public sealed class MediaServiceTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly MediaService _service;

    public MediaServiceTests()
    {
        _service = new MediaService(_test.Time);
        _test.CreateFolders("123-123_001", "123-123_002", "My Media");
    }

    public void Dispose() => _test.Dispose();

    private string Folder(params string[] parts) => Path.Combine([_test.Root, .. parts]);

    private IReadOnlyList<Media> Active()
    {
        using var scope = _test.Session.Database.Open();
        return new MediaRepository(scope).ListActive();
    }

    [Fact]
    public void Adds_direct_children_of_the_root_with_status_new_and_audit()
    {
        var result = _service.Add(_test.Session, [Folder("123-123_001"), Folder("My Media") + Path.DirectorySeparatorChar]);

        Assert.Empty(result.Rejected);
        Assert.Equal(["123-123_001", "My Media"], result.Added.Select(m => m.MediaId));
        var media = Active().Single(m => m.MediaId == "My Media");
        Assert.Equal(@"\My Media\", media.RelativePath);
        Assert.Equal(MediaStatus.New, media.Status);
        Assert.Equal(@"CORP\jdoe", media.AddedBy);
        Assert.Equal(_test.Time.GetUtcNow(), media.AddedAtUtc);
        Assert.Equal(2, _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.MediaAdded] }).Count);
    }

    [Fact]
    public void Media_id_uses_the_folder_name_as_on_disk()
    {
        // Folder names are matched case-insensitively (Windows semantics); the stored Media ID keeps the on-disk casing.
        var result = _service.Add(_test.Session, [Folder("my media")]);

        Assert.Equal("My Media", Assert.Single(result.Added).MediaId);
    }

    [Fact]
    public void Nested_folder_and_folder_outside_root_are_rejected()
    {
        Directory.CreateDirectory(Folder("123-123_001", "sub"));
        var outside = _test.Temp.Combine("Elsewhere");
        Directory.CreateDirectory(outside);

        var result = _service.Add(_test.Session, [Folder("123-123_001", "sub"), outside]);

        Assert.Empty(result.Added);
        Assert.All(result.Rejected, r => Assert.Contains("directly inside the root folder", r.Reason));
    }

    [Fact]
    public void Missing_folder_system_folder_and_duplicates_are_rejected()
    {
        _test.CreateFolders("$RECYCLE.BIN");
        _service.Add(_test.Session, [Folder("123-123_001")]);

        var result = _service.Add(_test.Session,
            [Folder("nope"), Folder("$RECYCLE.BIN"), Folder("123-123_001"), Folder("123-123_002"), Folder("123-123_002")]);

        Assert.Equal(["123-123_002"], result.Added.Select(m => m.MediaId));
        Assert.Contains(result.Rejected, r => r.Reason.Contains("does not exist"));
        Assert.Contains(result.Rejected, r => r.Reason.Contains("system folder"));
        Assert.Contains(result.Rejected, r => r.Reason.Contains("already in this inventory"));
        Assert.Contains(result.Rejected, r => r.Reason.Contains("more than once"));
    }

    [Fact]
    public void Delete_removes_scan_data_keeps_history_and_audits()
    {
        var key = _service.Add(_test.Session, [Folder("123-123_001")]).Added[0].MediaKey;
        long scanId;
        using (var scope = _test.Session.Database.Open())
        {
            var root = ScanRows.AddFolder(scope, key, @"\123-123_001\");
            ScanRows.AddFile(scope, key, root, "a.msg", 100);
            ScanRows.AddFile(scope, key, root, "b.pdf", 50);
            scanId = new ScanLogRepository(scope).Start(new ScanLogEntry
            {
                MediaKey = key, MediaId = "123-123_001", StartedAtUtc = _test.Time.GetUtcNow(),
                UserName = "u", MachineName = "m", AppVersion = "0.1.0", EnumThreads = 4, HashThreads = 4,
            });
        }

        var removed = _service.Delete(_test.Session, key);

        Assert.Equal(new ScanTotals(1, 2, 150, 0, 0), removed);
        Assert.Empty(Active());
        using var check = _test.Session.Database.Open();
        var deleted = new MediaRepository(check).Get(key)!;
        Assert.True(deleted.IsDeleted);
        Assert.Equal(@"CORP\jdoe", deleted.DeletedBy);
        Assert.Equal(ScanTotals.Zero, new ScanDataRepository(check).ComputeTotals(key));
        Assert.Equal(scanId, Assert.Single(new ScanLogRepository(check).ListByMedia(key)).ScanId);
        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.MediaDeleted] }).Single();
        Assert.Equal("123-123_001", audit.MediaId);
        Assert.Contains("\"fileCount\":2", audit.Details);
        Assert.True(Directory.Exists(Folder("123-123_001"))); // files on disk untouched
    }

    [Theory]
    [InlineData(MediaStatus.Queued)]
    [InlineData(MediaStatus.Scanning)]
    [InlineData(MediaStatus.Hashing)]
    [InlineData(MediaStatus.Paused)]
    public void Delete_is_refused_while_scanning(MediaStatus status)
    {
        var key = _service.Add(_test.Session, [Folder("123-123_001")]).Added[0].MediaKey;
        using (var scope = _test.Session.Database.Open())
        {
            new MediaRepository(scope).SetStatus(key, status);
        }

        var ex = Assert.Throws<InvalidOperationException>(() => _service.Delete(_test.Session, key));
        Assert.Contains("Cancel the scan", ex.Message);
        Assert.Single(Active());
    }

    [Fact]
    public void Deleted_media_can_be_added_again_as_a_new_row()
    {
        var first = _service.Add(_test.Session, [Folder("123-123_001")]).Added[0].MediaKey;
        _service.Delete(_test.Session, first);

        var second = Assert.Single(_service.Add(_test.Session, [Folder("123-123_001")]).Added);

        Assert.NotEqual(first, second.MediaKey);
        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.MediaAdded] }).First();
        Assert.Contains("\"previouslyDeleted\":true", audit.Details);
    }
}

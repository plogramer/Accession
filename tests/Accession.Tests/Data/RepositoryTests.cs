using Accession.Core.Model;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Tests.Data;

public sealed class RepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly TestInventory _inventory = new();

    public void Dispose() => _inventory.Dispose();

    private long AddMedia(string mediaId)
    {
        using var scope = _inventory.Database.Open();
        return new MediaRepository(scope).Insert(mediaId, $@"\{mediaId}\", T0, @"CORP\jdoe");
    }

    // ---- InventoryConfig ----

    [Fact]
    public void Config_matter_root_and_last_opened_can_be_updated()
    {
        using var scope = _inventory.Database.Open();
        var repo = new InventoryConfigRepository(scope);

        repo.UpdateMatter(new MatterInfo("Globex", "GLX", "Patent Review", "2026-042", null, "https://pm/42"));
        repo.UpdateRootPath(@"\\nas02\cases\GLX\Media");
        repo.UpdateLastOpened(T0, @"CORP\asmith", @"\\nas02\cases\GLX\Media\inv.sqlite");

        var config = repo.Get();
        Assert.Equal("Globex", config.ClientName);
        Assert.Equal("GLX", config.ClientCode);
        Assert.Equal("Patent Review", config.MatterName);
        Assert.Equal("2026-042", config.MatterCode);
        Assert.Null(config.Description);
        Assert.Equal("https://pm/42", config.MatterUrl);
        Assert.Equal(@"\\nas02\cases\GLX\Media", config.RootPath);
        Assert.Equal(T0, config.LastOpenedAtUtc);
        Assert.Equal(@"CORP\asmith", config.LastOpenedBy);
        Assert.Equal(@"\\nas02\cases\GLX\Media\inv.sqlite", config.LastDbPath);
    }

    // ---- Media ----

    [Fact]
    public void Media_insert_and_read_back()
    {
        var key = AddMedia("123-123_001");

        using var scope = _inventory.Database.Open();
        var media = new MediaRepository(scope).Get(key)!;

        Assert.Equal("123-123_001", media.MediaId);
        Assert.Equal(@"\123-123_001\", media.RelativePath);
        Assert.Equal(MediaStatus.New, media.Status);
        Assert.Null(media.StatusBeforeMissing);
        Assert.Equal(T0, media.AddedAtUtc);
        Assert.Equal(0, media.ScanCount);
        Assert.False(media.IsDeleted);
        Assert.Null(media.LastScanStartedUtc);
        Assert.Equal("New", scope.Connection.ExecuteScalar<string>("SELECT Status FROM Media WHERE MediaKey = @key", new { key }));
    }

    [Fact]
    public void Media_ids_are_unique_among_active_media_ignoring_case()
    {
        AddMedia("My Media");

        var ex = Assert.Throws<SqliteException>(() => AddMedia("MY MEDIA"));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Fact]
    public void Deleted_media_id_can_be_added_again()
    {
        var first = AddMedia("123-123_003");
        using (var scope = _inventory.Database.Open())
        {
            var repo = new MediaRepository(scope);
            repo.SoftDelete(first, T0, @"CORP\jdoe");
            Assert.Contains("123-123_003", repo.ListDeletedMediaIds());
        }

        var second = AddMedia("123-123_003");

        using var check = _inventory.Database.Open();
        var repo2 = new MediaRepository(check);
        Assert.NotEqual(first, second);
        Assert.Equal(second, repo2.FindActive("123-123_003")!.MediaKey);
        Assert.True(repo2.Get(first)!.IsDeleted);
        Assert.Equal(@"CORP\jdoe", repo2.Get(first)!.DeletedBy);
        Assert.Empty(repo2.ListDeletedMediaIds()); // active again, so not "previously deleted"
        Assert.Single(repo2.ListActive());
    }

    [Fact]
    public void ListActive_excludes_deleted_and_orders_by_media_id()
    {
        AddMedia("B");
        var deleted = AddMedia("C");
        AddMedia("A");
        using var scope = _inventory.Database.Open();
        var repo = new MediaRepository(scope);
        repo.SoftDelete(deleted, T0, "u");

        Assert.Equal(["A", "B"], repo.ListActive().Select(m => m.MediaId));
    }

    [Fact]
    public void FindActive_ignores_case()
    {
        var key = AddMedia("Custodian Laptop");
        using var scope = _inventory.Database.Open();

        Assert.Equal(key, new MediaRepository(scope).FindActive("custodian laptop")!.MediaKey);
        Assert.Null(new MediaRepository(scope).FindActive("other"));
    }

    [Fact]
    public void Missing_status_remembers_and_restores_previous_status()
    {
        var key = AddMedia("M1");
        using var scope = _inventory.Database.Open();
        var repo = new MediaRepository(scope);
        repo.SetStatus(key, MediaStatus.CompletedWithErrors);

        repo.MarkMissing(key);
        repo.MarkMissing(key); // second call must not overwrite the saved status
        var missing = repo.Get(key)!;
        Assert.Equal(MediaStatus.Missing, missing.Status);
        Assert.Equal(MediaStatus.CompletedWithErrors, missing.StatusBeforeMissing);

        repo.RestoreFromMissing(key);
        var restored = repo.Get(key)!;
        Assert.Equal(MediaStatus.CompletedWithErrors, restored.Status);
        Assert.Null(restored.StatusBeforeMissing);
    }

    [Fact]
    public void Scan_markers_and_totals_are_updated()
    {
        var key = AddMedia("M1");
        using var scope = _inventory.Database.Open();
        var repo = new MediaRepository(scope);

        repo.MarkScanStarted(key, T0, isFullScan: true);
        repo.MarkScanStarted(key, T0.AddHours(1), isFullScan: false);
        repo.MarkScanCompleted(key, T0.AddHours(2));
        repo.UpdateTotals(key, new ScanTotals(10, 1000, 5_000_000_000, 990, 3));

        var media = repo.Get(key)!;
        Assert.Equal(1, media.ScanCount);
        Assert.Equal(T0.AddHours(1), media.LastScanStartedUtc);
        Assert.Equal(T0.AddHours(2), media.LastScanCompletedUtc);
        Assert.Equal(10, media.FolderCount);
        Assert.Equal(1000, media.FileCount);
        Assert.Equal(5_000_000_000, media.TotalBytes);
        Assert.Equal(990, media.HashedCount);
        Assert.Equal(3, media.ErrorCount);
    }

    // ---- ScanLog ----

    [Fact]
    public void Scan_log_start_finish_and_history()
    {
        var key = AddMedia("M1");
        using var scope = _inventory.Database.Open();
        var repo = new ScanLogRepository(scope);

        var first = repo.Start(NewScan(key, ScanType.Full, T0));
        repo.Finish(first, T0.AddMinutes(5), ScanOutcome.Cancelled, new ScanTotals(1, 2, 3, 0, 0), "user cancelled");
        var second = repo.Start(NewScan(key, ScanType.Resume, T0.AddHours(1)));

        var history = repo.ListByMedia(key);
        Assert.Equal([second, first], history.Select(h => h.ScanId));
        Assert.Equal(ScanType.Resume, history[0].ScanType);
        Assert.Null(history[0].Outcome);
        Assert.Equal(ScanOutcome.Cancelled, history[1].Outcome);
        Assert.Equal(T0.AddMinutes(5), history[1].EndedAtUtc);
        Assert.Equal(2, history[1].FileCount);
        Assert.Equal("user cancelled", history[1].Notes);
        Assert.Equal("M1", history[1].MediaId);
        Assert.Equal([second], repo.ListUnfinished().Select(s => s.ScanId));
    }

    private static ScanLogEntry NewScan(long mediaKey, ScanType type, DateTimeOffset at) => new()
    {
        MediaKey = mediaKey,
        MediaId = "M1",
        ScanType = type,
        StartedAtUtc = at,
        UserName = @"CORP\jdoe",
        MachineName = "WS-114",
        AppVersion = "0.1.0",
        EnumThreads = 4,
        HashThreads = 4,
    };

    // ---- ScanError ----

    [Fact]
    public void Scan_errors_filter_and_page()
    {
        var m1 = AddMedia("M1");
        var m2 = AddMedia("M2");
        using var scope = _inventory.Database.Open();
        var repo = new ScanErrorRepository(scope);
        for (var i = 0; i < 5; i++)
        {
            repo.Insert(NewError(m1, ScanErrorType.AccessDenied, ScanErrorSeverity.Error, $@"\M1\denied{i}\"));
        }

        repo.Insert(NewError(m1, ScanErrorType.ReparsePointSkipped, ScanErrorSeverity.Info, @"\M1\link\"));
        repo.Insert(NewError(m2, ScanErrorType.FileLocked, ScanErrorSeverity.Error, @"\M2\a.pst"));

        var page1 = repo.List(new ScanErrorQuery { MediaKey = m1, PageSize = 3 });
        var page2 = repo.List(new ScanErrorQuery { MediaKey = m1, PageSize = 3, AfterErrorId = page1[^1].ErrorId });
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.Empty(page1.Select(e => e.ErrorId).Intersect(page2.Select(e => e.ErrorId)));

        var locked = Assert.Single(repo.List(new ScanErrorQuery { ErrorTypes = [ScanErrorType.FileLocked] }));
        Assert.Equal(m2, locked.MediaKey);
        Assert.Equal(ScanItemType.File, locked.ItemType);
        Assert.Equal(32, locked.ErrorCode);
        Assert.Equal(T0, locked.OccurredAtUtc);

        Assert.Single(repo.List(new ScanErrorQuery { Severities = [ScanErrorSeverity.Info] }));
        Assert.Equal(5, repo.CountByMedia(m1));
        Assert.Equal(6, repo.CountByMedia(m1, includeInfo: true));
    }

    private static ScanErrorEntry NewError(long mediaKey, ScanErrorType type, ScanErrorSeverity severity, string path) => new()
    {
        MediaKey = mediaKey,
        RelativePath = path,
        ItemType = path.EndsWith('\\') ? ScanItemType.Folder : ScanItemType.File,
        ErrorType = type,
        Severity = severity,
        ErrorCode = type == ScanErrorType.FileLocked ? 32 : 5,
        Message = type.ToString(),
        OccurredAtUtc = T0,
    };

    // ---- Summary and scan data ----

    [Fact]
    public void Summary_is_rebuilt_per_extension_and_totals_are_computed()
    {
        var m1 = AddMedia("M1");
        var m2 = AddMedia("M2");
        using var scope = _inventory.Database.Open();
        var root = ScanRows.AddFolder(scope, m1, @"\M1\");
        var sub = ScanRows.AddFolder(scope, m1, @"\M1\mail\", root);
        ScanRows.AddFile(scope, m1, sub, "a.msg", 100, sha1: new string('a', 40));
        ScanRows.AddFile(scope, m1, sub, "b.MSG", 200);
        ScanRows.AddFile(scope, m1, root, "README", 5, sha1: new string('b', 40));
        var m2Root = ScanRows.AddFolder(scope, m2, @"\M2\");
        ScanRows.AddFile(scope, m2, m2Root, "c.pdf", 1000);
        new ScanErrorRepository(scope).Insert(NewError(m1, ScanErrorType.AccessDenied, ScanErrorSeverity.Error, @"\M1\x\"));
        new ScanErrorRepository(scope).Insert(NewError(m1, ScanErrorType.ReparsePointSkipped, ScanErrorSeverity.Info, @"\M1\y\"));

        var summaries = new SummaryRepository(scope);
        summaries.RebuildForMedia(m1);
        summaries.RebuildForMedia(m1); // idempotent
        summaries.RebuildForMedia(m2);

        var m1Rows = summaries.List([m1]);
        Assert.Equal([new ExtensionSummary(m1, "", 1, 5), new ExtensionSummary(m1, "msg", 2, 300)], m1Rows);
        Assert.Equal(3, summaries.List().Count);

        var totals = new ScanDataRepository(scope).ComputeTotals(m1);
        Assert.Equal(new ScanTotals(FolderCount: 2, FileCount: 3, TotalBytes: 305, HashedCount: 2, ErrorCount: 1), totals);
    }

    [Fact]
    public void DeleteForMedia_removes_scan_data_but_keeps_scan_log_and_other_media()
    {
        var m1 = AddMedia("M1");
        var m2 = AddMedia("M2");
        using var scope = _inventory.Database.Open();
        var root = ScanRows.AddFolder(scope, m1, @"\M1\");
        var sub = ScanRows.AddFolder(scope, m1, @"\M1\sub\", root);
        ScanRows.AddFile(scope, m1, sub, "a.txt", 1);
        var m2Root = ScanRows.AddFolder(scope, m2, @"\M2\");
        ScanRows.AddFile(scope, m2, m2Root, "b.txt", 1);
        new ScanErrorRepository(scope).Insert(NewError(m1, ScanErrorType.AccessDenied, ScanErrorSeverity.Error, @"\M1\x\"));
        new SummaryRepository(scope).RebuildForMedia(m1);
        var scanId = new ScanLogRepository(scope).Start(NewScan(m1, ScanType.Full, T0));

        new ScanDataRepository(scope).DeleteForMedia(m1);

        Assert.Equal(ScanTotals.Zero, new ScanDataRepository(scope).ComputeTotals(m1));
        Assert.Empty(new SummaryRepository(scope).List([m1]));
        Assert.Equal(1, new ScanDataRepository(scope).ComputeTotals(m2).FileCount);
        Assert.Equal(scanId, Assert.Single(new ScanLogRepository(scope).ListByMedia(m1)).ScanId);
    }

    [Fact]
    public void Repositories_join_the_scope_transaction()
    {
        using (var scope = _inventory.Database.Open())
        using (scope.BeginTransaction())
        {
            new MediaRepository(scope).Insert("Rolled", @"\Rolled\", T0, "u");
        }

        using var check = _inventory.Database.Open();
        Assert.Empty(new MediaRepository(check).ListActive());
    }
}

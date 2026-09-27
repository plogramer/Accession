using System.Security.Cryptography;
using System.Text;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Scanning;
using Accession.Data.Audit;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using Accession.Data.Scanning;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Scanning;

public sealed class ScanCoordinatorTests : IAsyncDisposable
{
    private readonly TestSession _test = new();
    private readonly FakeLister _lister = new();
    private readonly FakeHasher _hasher = new();
    private readonly ScanCoordinator _coordinator;
    private readonly List<string> _autoPaused = [];

    public ScanCoordinatorTests()
    {
        _coordinator = new ScanCoordinator(_test.Session, _lister, _hasher, () => new ScanOptions(3, 3, 50),
            new App(), TimeProvider.System, NullLogger<ScanCoordinator>.Instance, retryDelays: [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        _coordinator.AutoPaused += (_, message) => _autoPaused.Add(message);
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        _test.Dispose();
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    // ---------- helpers ----------

    private string MediaPath(string mediaId, params string[] parts) => Path.Combine([_test.Root, mediaId, .. parts]);

    private void WriteFile(string mediaId, string relative, string content)
    {
        var path = MediaPath(mediaId, relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Creates a small media tree and registers it. Returns the media key.</summary>
    private long CreateMedia(string mediaId = "M1")
    {
        WriteFile(mediaId, "readme.txt", "hello");
        WriteFile(mediaId, "mail/a.msg", "message a");
        WriteFile(mediaId, "mail/b.MSG", "message b");
        WriteFile(mediaId, "mail/2019/c.pst", new string('x', 5000));
        WriteFile(mediaId, "docs/report.pdf", "pdf content");
        Directory.CreateDirectory(MediaPath(mediaId, "empty"));
        return new MediaService(_test.Time).Add(_test.Session, [MediaPath(mediaId)]).Added[0].MediaKey;
    }

    private static string Sha1(string content) => Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(content)));

    private async Task WaitIdleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_coordinator.IsBusy)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Scan did not finish.");
            }

            await Task.Delay(20);
        }
    }

    private async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met.");
            }

            await Task.Delay(20);
        }
    }

    private async Task ScanAsync(long mediaKey, ScanType type = ScanType.Full)
    {
        _coordinator.Enqueue(mediaKey, type);
        await WaitIdleAsync();
    }

    private Media GetMedia(long key)
    {
        using var scope = _test.Session.Database.Open();
        return new MediaRepository(scope).Get(key)!;
    }

    private Dictionary<string, (long Size, string? Sha1, long HashStatus)> Files(long mediaKey)
    {
        using var scope = _test.Session.Database.Open();
        return scope.Connection.Query<(string Path, long Size, string? Sha1, long HashStatus)>(
                "SELECT fo.RelativePath || f.Name, f.SizeBytes, f.Sha1, f.HashStatus FROM File f JOIN Folder fo ON fo.FolderId = f.FolderId WHERE f.MediaKey = @mediaKey",
                new { mediaKey })
            .ToDictionary(r => r.Path, r => (r.Size, r.Sha1, r.HashStatus));
    }

    private List<ScanErrorEntry> Errors(long mediaKey)
    {
        using var scope = _test.Session.Database.Open();
        return new ScanErrorRepository(scope).List(new ScanErrorQuery { MediaKey = mediaKey }).ToList();
    }

    private List<ScanLogEntry> ScanLog(long mediaKey)
    {
        using var scope = _test.Session.Database.Open();
        return new ScanLogRepository(scope).ListByMedia(mediaKey).ToList();
    }

    private List<AuditAction> AuditActions(string mediaId) =>
        _test.Session.Audit.Query(new AuditQuery { MediaId = mediaId }).Select(a => a.Action).Reverse().ToList();

    // ---------- full scan ----------

    [Fact]
    public async Task Full_scan_records_folders_files_hashes_and_totals()
    {
        var key = CreateMedia();

        await ScanAsync(key);

        var media = GetMedia(key);
        Assert.Equal(MediaStatus.Completed, media.Status);
        Assert.Equal(1, media.ScanCount);
        Assert.Equal(5, media.FolderCount); // M1, mail, mail\2019, docs, empty
        Assert.Equal(5, media.FileCount);
        Assert.Equal(5 + 9 + 9 + 5000 + 11, media.TotalBytes);
        Assert.Equal(5, media.HashedCount);
        Assert.Equal(0, media.ErrorCount);
        Assert.NotNull(media.LastScanCompletedUtc);

        var files = Files(key);
        Assert.Equal(Sha1("hello"), files[@"\M1\readme.txt"].Sha1);
        Assert.Equal(Sha1("message a"), files[@"\M1\mail\a.msg"].Sha1);
        Assert.Equal(Sha1(new string('x', 5000)), files[@"\M1\mail\2019\c.pst"].Sha1);
        Assert.All(files.Values, f => Assert.Equal((long)HashStatus.Hashed, f.HashStatus));

        using var scope = _test.Session.Database.Open();
        var folders = scope.Connection.Query<(string RelativePath, long IsEnumerated)>(
            "SELECT RelativePath, IsEnumerated FROM Folder WHERE MediaKey = @key ORDER BY RelativePath", new { key }).ToList();
        Assert.Equal([@"\M1\", @"\M1\docs\", @"\M1\empty\", @"\M1\mail\", @"\M1\mail\2019\"], folders.Select(f => f.RelativePath));
        Assert.All(folders, f => Assert.Equal(1, f.IsEnumerated));
        Assert.Contains(new ExtensionSummary(key, "msg", 2, 18), new SummaryRepository(scope).List([key]));

        var log = Assert.Single(ScanLog(key));
        Assert.Equal(ScanType.Full, log.ScanType);
        Assert.Equal(ScanOutcome.Completed, log.Outcome);
        Assert.Equal(5, log.FileCount);
        Assert.Equal(3, log.EnumThreads);
        Assert.Equal([AuditAction.MediaAdded, AuditAction.ScanQueued, AuditAction.ScanStarted, AuditAction.ScanCompleted], AuditActions("M1"));
    }

    [Fact]
    public async Task Timestamps_are_captured_from_the_listing()
    {
        var key = CreateMedia();
        var modified = new DateTime(2019, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(MediaPath("M1", "readme.txt"), modified);

        await ScanAsync(key);

        using var scope = _test.Session.Database.Open();
        Assert.Equal("2019-05-06T07:08:09.0000000Z",
            scope.Connection.ExecuteScalar<string>("SELECT ModifiedUtc FROM File WHERE Name = 'readme.txt'"));
    }

    [Fact]
    public async Task Rescan_replaces_previous_results()
    {
        var key = CreateMedia();
        await ScanAsync(key);
        File.WriteAllText(MediaPath("M1", "readme.txt"), "changed");
        File.Delete(MediaPath("M1", "docs", "report.pdf"));

        await ScanAsync(key);

        var files = Files(key);
        Assert.Equal(4, files.Count);
        Assert.Equal(Sha1("changed"), files[@"\M1\readme.txt"].Sha1);
        Assert.Equal(2, GetMedia(key).ScanCount);
        Assert.Equal(2, ScanLog(key).Count);
    }

    [Fact]
    public async Task Queued_media_are_scanned_one_after_another()
    {
        var m1 = CreateMedia("M1");
        var m2 = CreateMedia("M2");
        var m3 = CreateMedia("M3");

        _coordinator.Enqueue(m1, ScanType.Full);
        _coordinator.Enqueue(m2, ScanType.Full);
        _coordinator.Enqueue(m3, ScanType.Full);
        await WaitIdleAsync();

        var logs = new[] { m1, m2, m3 }.Select(k => Assert.Single(ScanLog(k))).ToList();
        Assert.True(logs[0].EndedAtUtc <= logs[1].StartedAtUtc);
        Assert.True(logs[1].EndedAtUtc <= logs[2].StartedAtUtc);
        Assert.All(new[] { m1, m2, m3 }, k => Assert.Equal(MediaStatus.Completed, GetMedia(k).Status));
    }

    [Fact]
    public async Task Missing_media_folder_marks_media_missing()
    {
        var key = CreateMedia();
        Directory.Delete(MediaPath("M1"), recursive: true);

        await ScanAsync(key);

        Assert.Equal(MediaStatus.Missing, GetMedia(key).Status);
        Assert.Contains(AuditAction.ScanFailed, AuditActions("M1"));
    }

    // ---------- errors ----------

    [Fact]
    public async Task Denied_folder_is_recorded_with_an_error_and_the_scan_continues()
    {
        var key = CreateMedia();
        var denied = MediaPath("M1", "mail");
        _lister.FailWith = path => path == denied ? new UnauthorizedAccessException("Access to the path is denied.") : null;

        await ScanAsync(key);

        var media = GetMedia(key);
        Assert.Equal(MediaStatus.CompletedWithErrors, media.Status);
        Assert.Equal(1, media.ErrorCount);
        var error = Assert.Single(Errors(key));
        Assert.Equal(ScanErrorType.AccessDenied, error.ErrorType);
        Assert.Equal(ScanItemType.Folder, error.ItemType);
        Assert.Equal(@"\M1\mail\", error.RelativePath);
        Assert.Equal(["\\M1\\docs\\report.pdf", "\\M1\\readme.txt"], Files(key).Keys.Order());
        Assert.Equal(ScanOutcome.CompletedWithErrors, ScanLog(key)[0].Outcome);
    }

    [Fact]
    public async Task Locked_file_is_logged_and_retry_failed_hashes_it_later()
    {
        var key = CreateMedia();
        var locked = MediaPath("M1", "mail", "a.msg");
        _hasher.FailWith = path => path == locked ? Win32Errors.Create(32, "The process cannot access the file because it is being used by another process.") : null;

        await ScanAsync(key);

        Assert.Equal((long)HashStatus.Error, Files(key)[@"\M1\mail\a.msg"].HashStatus);
        var error = Assert.Single(Errors(key));
        Assert.Equal(ScanErrorType.FileLocked, error.ErrorType);
        Assert.Equal(32, error.ErrorCode);
        Assert.Equal(MediaStatus.CompletedWithErrors, GetMedia(key).Status);

        _hasher.FailWith = _ => null;
        _hasher.Calls.Clear();
        await ScanAsync(key, ScanType.RetryFailed);

        Assert.Equal([locked], _hasher.Calls);
        Assert.Equal(Sha1("message a"), Files(key)[@"\M1\mail\a.msg"].Sha1);
        Assert.Empty(Errors(key));
        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status);
        Assert.Equal(1, GetMedia(key).ScanCount);
        Assert.Equal(ScanType.RetryFailed, ScanLog(key)[0].ScanType);
    }

    [Fact]
    public async Task File_changed_during_scan_keeps_hash_and_gets_a_warning()
    {
        var key = CreateMedia();
        var changing = MediaPath("M1", "readme.txt");
        _hasher.Alter = (path, result) => path == changing ? result with { SizeAfter = result.SizeAfter + 10 } : result;

        await ScanAsync(key);

        Assert.Equal(Sha1("hello"), Files(key)[@"\M1\readme.txt"].Sha1);
        var warning = Assert.Single(Errors(key));
        Assert.Equal(ScanErrorType.ChangedDuringScan, warning.ErrorType);
        Assert.Equal(ScanErrorSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task Reparse_points_are_recorded_but_not_followed()
    {
        var key = CreateMedia();
        var outside = _test.Temp.Combine("Outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "not part of the media");
        try
        {
            Directory.CreateSymbolicLink(MediaPath("M1", "link"), outside);
            File.CreateSymbolicLink(MediaPath("M1", "filelink.txt"), Path.Combine(outside, "secret.txt"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        await ScanAsync(key);

        var files = Files(key);
        Assert.DoesNotContain(files.Keys, k => k.Contains("secret"));
        Assert.Equal((long)HashStatus.Skipped, files[@"\M1\filelink.txt"].HashStatus);
        Assert.Null(files[@"\M1\filelink.txt"].Sha1);
        Assert.DoesNotContain(_hasher.Calls, c => c.Contains("filelink"));
        var errors = Errors(key);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ScanErrorType.ReparsePointSkipped, e.ErrorType));
        Assert.All(errors, e => Assert.Equal(ScanErrorSeverity.Info, e.Severity));
        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status); // Info entries are not errors
    }

    // ---------- pause / cancel / resume ----------

    [Fact]
    public async Task Pause_and_resume()
    {
        var key = CreateMedia();
        using var hold = new ManualResetEventSlim(false);
        var holding = MediaPath("M1", "mail", "2019", "c.pst");
        _hasher.Before = (path, ct) =>
        {
            if (path == holding)
            {
                hold.Wait(ct);
            }
        };

        _coordinator.Enqueue(key, ScanType.Full);
        await WaitForAsync(() => _hasher.Calls.Contains(holding));
        _coordinator.Pause();

        Assert.Equal(CoordinatorState.Paused, _coordinator.State);
        Assert.Equal(MediaStatus.Paused, GetMedia(key).Status);
        hold.Set();
        _coordinator.Resume();
        await WaitIdleAsync();

        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status);
        var actions = AuditActions("M1");
        Assert.Contains(AuditAction.ScanPaused, actions);
        Assert.Contains(AuditAction.ScanResumed, actions);
    }

    [Fact]
    public async Task Cancel_leaves_media_incomplete_and_resume_finishes_without_rehashing()
    {
        var key = CreateMedia();
        for (var i = 0; i < 40; i++)
        {
            WriteFile("M1", $"bulk/f{i:00}.txt", $"file {i}");
        }

        using var hold = new ManualResetEventSlim(false);
        var holding = MediaPath("M1", "mail", "2019", "c.pst");
        _hasher.Before = (path, ct) =>
        {
            if (path == holding)
            {
                hold.Wait(ct);
            }
        };

        _coordinator.Enqueue(key, ScanType.Full);
        await WaitForAsync(() => _hasher.Calls.Contains(holding));
        _coordinator.Cancel();
        await WaitIdleAsync();

        Assert.Equal(MediaStatus.Incomplete, GetMedia(key).Status);
        Assert.Equal(ScanOutcome.Cancelled, ScanLog(key)[0].Outcome);
        Assert.Contains(AuditAction.ScanCancelled, AuditActions("M1"));
        var hashedBefore = Files(key).Where(f => f.Value.HashStatus == (long)HashStatus.Hashed).Select(f => f.Key).ToList();

        _hasher.Before = (_, _) => { };
        _hasher.Calls.Clear();
        await ScanAsync(key, ScanType.Resume);

        var files = Files(key);
        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status);
        Assert.Equal(45, files.Count);
        Assert.All(files.Values, f => Assert.Equal((long)HashStatus.Hashed, f.HashStatus));
        Assert.Equal(Sha1("file 7"), files[@"\M1\bulk\f07.txt"].Sha1);
        Assert.DoesNotContain(_hasher.Calls, call => hashedBefore.Any(h => call.EndsWith(h.Replace('\\', Path.DirectorySeparatorChar), StringComparison.Ordinal)));
        Assert.Equal(1, GetMedia(key).ScanCount);
        Assert.Equal([ScanType.Resume, ScanType.Full], ScanLog(key).Select(l => l.ScanType));
    }

    [Fact]
    public async Task Stop_interrupts_the_running_scan_and_clears_the_queue()
    {
        var m1 = CreateMedia("M1");
        var m2 = CreateMedia("M2");
        using var hold = new ManualResetEventSlim(false);
        _hasher.Before = (_, ct) => hold.Wait(ct);

        _coordinator.Enqueue(m1, ScanType.Full);
        _coordinator.Enqueue(m2, ScanType.Full);
        await WaitForAsync(() => !_hasher.Calls.IsEmpty);
        await _coordinator.StopAsync();

        Assert.Equal(MediaStatus.Incomplete, GetMedia(m1).Status);
        Assert.Equal(ScanOutcome.Interrupted, ScanLog(m1)[0].Outcome);
        Assert.Equal(MediaStatus.New, GetMedia(m2).Status);
        Assert.Empty(ScanLog(m2));
    }

    // ---------- network ----------

    [Fact]
    public async Task Transient_network_error_is_retried_without_errors()
    {
        var key = CreateMedia();
        var failures = 2;
        var mail = MediaPath("M1", "mail");
        _lister.FailWith = path => path == mail && Interlocked.Decrement(ref failures) >= 0 ? Win32Errors.Create(64, "The specified network name is no longer available.") : null;

        await ScanAsync(key);

        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status);
        Assert.Empty(Errors(key));
        Assert.Empty(_autoPaused);
        Assert.Equal(5, Files(key).Count);
    }

    [Fact]
    public async Task Persistent_network_error_pauses_the_scan_until_resumed()
    {
        var key = CreateMedia();
        var networkDown = true;
        _lister.FailWith = _ => Volatile.Read(ref networkDown) ? Win32Errors.Create(53, "The network path was not found.") : null;

        _coordinator.Enqueue(key, ScanType.Full);
        await WaitForAsync(() => _coordinator.State == CoordinatorState.Paused && _autoPaused.Count > 0);

        Assert.Single(_autoPaused);
        Assert.Equal(MediaStatus.Paused, GetMedia(key).Status);

        Volatile.Write(ref networkDown, false);
        _coordinator.Resume();
        await WaitIdleAsync();

        Assert.Equal(MediaStatus.Completed, GetMedia(key).Status);
        Assert.Equal(5, Files(key).Count);
        Assert.Contains("network unavailable", ScanLog(key)[0].Notes);
    }

    // ---------- queue rules ----------

    [Fact]
    public void Queue_rules_are_enforced()
    {
        var key = CreateMedia();

        var resume = Assert.Throws<InvalidOperationException>(() => _coordinator.Enqueue(key, ScanType.Resume));
        Assert.Contains("no incomplete scan", resume.Message);
        var retry = Assert.Throws<InvalidOperationException>(() => _coordinator.Enqueue(key, ScanType.RetryFailed));
        Assert.Contains("not been scanned", retry.Message);
    }

    [Fact]
    public async Task Waiting_items_can_be_reordered_and_removed()
    {
        var m1 = CreateMedia("M1");
        var m2 = CreateMedia("M2");
        var m3 = CreateMedia("M3");
        using var hold = new ManualResetEventSlim(false);
        _hasher.Before = (_, ct) => hold.Wait(ct);

        _coordinator.Enqueue(m1, ScanType.Full);
        await WaitForAsync(() => _coordinator.Current is not null);
        _coordinator.Enqueue(m2, ScanType.Full);
        _coordinator.Enqueue(m3, ScanType.Full);
        Assert.Throws<InvalidOperationException>(() => _coordinator.Enqueue(m2, ScanType.Full));

        _coordinator.MoveUp(m3);
        Assert.Equal([m3, m2], _coordinator.Waiting.Select(w => w.MediaKey));
        Assert.Equal(MediaStatus.Queued, GetMedia(m2).Status);

        Assert.True(_coordinator.Remove(m2));
        Assert.Equal(MediaStatus.New, GetMedia(m2).Status);
        Assert.Equal([m3], _coordinator.Waiting.Select(w => w.MediaKey));

        hold.Set();
        await WaitIdleAsync();
        Assert.Equal(MediaStatus.Completed, GetMedia(m3).Status);
        Assert.Empty(ScanLog(m2));
    }

    [Fact]
    public async Task Progress_reports_totals()
    {
        var key = CreateMedia();
        var snapshots = new List<ScanProgressSnapshot>();
        _coordinator.ProgressChanged += (_, s) => { lock (snapshots) { snapshots.Add(s); } };

        await ScanAsync(key);

        ScanProgressSnapshot last;
        lock (snapshots)
        {
            last = snapshots[^1];
        }

        Assert.Equal(5, last.FilesFound);
        Assert.Equal(5, last.FilesHashed);
        Assert.Equal(100, last.PercentByBytes);
        Assert.True(last.EnumerationDone);
    }

    // ---------- recovery ----------

    [Fact]
    public async Task Recovery_marks_unfinished_scans_incomplete_and_interrupted()
    {
        var key = CreateMedia();
        await ScanAsync(key);
        using (var scope = _test.Session.Database.Open())
        {
            new MediaRepository(scope).SetStatus(key, MediaStatus.Hashing);
            new ScanLogRepository(scope).Start(new ScanLogEntry
            {
                MediaKey = key, MediaId = "M1", ScanType = ScanType.Full, StartedAtUtc = _test.Time.GetUtcNow(),
                UserName = "u", MachineName = "m", AppVersion = "0.1.0", EnumThreads = 1, HashThreads = 1,
            });

            Assert.Equal(1, ScanRecovery.Recover(scope, _test.Time.GetUtcNow()));
        }

        Assert.Equal(MediaStatus.Incomplete, GetMedia(key).Status);
        Assert.Equal(ScanOutcome.Interrupted, ScanLog(key)[0].Outcome);
    }
}

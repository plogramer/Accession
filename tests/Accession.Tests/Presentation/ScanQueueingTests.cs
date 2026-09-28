using System.Diagnostics;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.MediaManagement;
using Accession.Data.Scanning;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>
/// Adding media with "Start scanning" must not freeze the app: queueing writes to the database, which can wait for a
/// running scan, so it happens in the background; and the burst of status changes reloads the screens once.
/// </summary>
public sealed class ScanQueueingTests : IAsyncDisposable
{
    private readonly TestSession _test = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly ScanHost _scans;
    private readonly IReadOnlyList<Media> _media;

    public ScanQueueingTests()
    {
        var folders = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            var folder = Path.Combine(_test.Root, $"M{i}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.txt"), $"file {i}");
            folders.Add(folder);
        }

        _media = new MediaService(_test.Time).Add(_test.Session, folders).Added;
        _scans = new ScanHost(_host, new TestSettings(), new App(), TimeProvider.System, NullLoggerFactory.Instance, new NoDialogs(), new InlineUiDispatcher());
        _host.Open(_test.Session);
    }

    public async ValueTask DisposeAsync()
    {
        await _scans.ShutdownAsync();
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public async Task Queueing_never_waits_for_the_database_on_the_calling_thread()
    {
        Assert.True(_scans.CanScan);
        await using var blocker = new SqliteConnection($"Data Source={_test.Session.DbPath}");
        await blocker.OpenAsync(TestContext.Current.CancellationToken);
        await using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE"; // like a scan writing a batch: holds the write lock
            await begin.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var watch = Stopwatch.StartNew();
        var queueing = _scans.EnqueueAsync(_media.Select(m => (m, ScanType.Full)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"The caller waited {watch.Elapsed.TotalMilliseconds:0} ms.");
        Assert.False(queueing.IsCompleted); // still waiting for the database, in the background

        await using (var rollback = blocker.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await queueing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken); // NoDialogs: a warning would fail it
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_scans.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.False(_scans.IsBusy); // all five were queued and scanned
    }

    [Fact]
    public void Queueing_many_reports_the_ones_that_cannot_be_queued_and_queues_the_rest()
    {
        var coordinator = _scans.Coordinator!;
        coordinator.Pause();
        var states = 0;
        coordinator.StateChanged += (_, _) => Interlocked.Increment(ref states);

        var problems = coordinator.EnqueueMany([(_media[0].MediaKey, ScanType.Full), (_media[0].MediaKey, ScanType.Full), (_media[1].MediaKey, ScanType.Resume), (999, ScanType.Full)]);

        Assert.Equal(["already in the scan queue", "no incomplete scan to resume", "does not exist"],
            problems.Select(p => p.Reason.Contains("already", StringComparison.Ordinal) ? "already in the scan queue"
                : p.Reason.Contains("resume", StringComparison.Ordinal) ? "no incomplete scan to resume" : "does not exist"));
        Assert.True(states >= 1);
    }

    [Fact]
    public void A_burst_of_media_changes_reloads_the_screens_once()
    {
        var ui = new QueuedUi();
        var host = new InventoryHost(ui);
        var raised = 0;
        host.MediaChanged += (_, _) => raised++;

        for (var i = 0; i < 100; i++)
        {
            host.NotifyMediaChanged(); // e.g. 100 media queued, each reporting "Queued"
        }

        Assert.Single(ui.Pending);
        ui.RunAll();
        Assert.Equal(1, raised);

        host.NotifyMediaChanged(); // a later change is not lost
        ui.RunAll();
        Assert.Equal(2, raised);
    }

    private sealed class QueuedUi : IUiDispatcher
    {
        public List<Action> Pending { get; } = [];

        public void Post(Action action) => Pending.Add(action);

        public void Defer(Action action) => Pending.Add(action);

        public T Invoke<T>(Func<T> action) => action();

        public void RunAll()
        {
            var actions = Pending.ToList();
            Pending.Clear();
            actions.ForEach(a => a());
        }
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

using System.Diagnostics;
using Accession.Core.Runtime;
using Accession.Data;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Data;

/// <summary>
/// With a rollback journal, a write waits for every reader, and every new read waits behind the write. The Dashboard's
/// long queries therefore give way to writes (adding media, queueing a scan): otherwise the app froze until they ended.
/// </summary>
public sealed class LongReadsTests : IDisposable
{
    // Never ends on its own, and keeps a read lock on the inventory (it reads a table on every row).
    private const string EndlessRead =
        "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n) SELECT COUNT(*) FROM n, InventoryConfig";

    private readonly TestSession _test = new();

    public void Dispose() => _test.Dispose();

    private Task<Exception?> StartLongRead(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var scope = _test.Session.Database.Open();
        using var lease = LongReads.Enter(_test.Session.DbPath, scope.Connection);
        using var cancel = cancellationToken.Register(() => LongReads.Interrupt(_test.Session.DbPath));
        try
        {
            scope.Connection.ExecuteScalar<long>(EndlessRead);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }, CancellationToken.None);

    private static async Task Started() => await Task.Delay(300, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_write_interrupts_the_long_read_instead_of_waiting_for_it()
    {
        var read = StartLongRead();
        await Started();

        var watch = Stopwatch.StartNew();
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            new MediaRepository(scope).Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            transaction.Commit();
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The write waited {watch.ElapsedMilliseconds} ms.");
        Assert.True(LongReads.IsInterrupted((await read)!));
    }

    [Fact]
    public async Task An_audit_entry_on_its_own_interrupts_it_too()
    {
        var read = StartLongRead();
        await Started();

        _test.Session.Audit.Write(Accession.Core.Model.AuditAction.ConfigUpdated);

        Assert.True(LongReads.IsInterrupted((await read)!));
    }

    [Fact]
    public async Task Cancelling_stops_the_query_itself()
    {
        using var cancel = new CancellationTokenSource();
        var read = StartLongRead(cancel.Token);
        await Started();

        await cancel.CancelAsync();

        Assert.True(LongReads.IsInterrupted((await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))!));
        Assert.Equal(0, LongReads.Interrupt(_test.Session.DbPath)); // nothing left registered
    }

    [Fact]
    public async Task The_dashboard_gives_way_and_calculates_again_afterwards()
    {
        using (var scope = _test.Session.Database.Open())
        {
            var media = new MediaRepository(scope).Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            var folder = ScanRows.AddFolder(scope, media, @"\M1\");
            ScanRows.AddFile(scope, media, folder, "a.txt", 10, sha1: new string('a', 40));
            ScanRows.AddFile(scope, media, folder, "b.txt", 10, sha1: new string('a', 40));
        }

        // An endless Duplicates query (a query override file) stands in for one that takes minutes.
        var overrides = _test.Temp.Combine("queries");
        Directory.CreateDirectory(overrides);
        var slow = Path.Combine(overrides, "Duplicates.sql");
        File.WriteAllText(slow, EndlessRead);
        var host = new InventoryHost(new InlineUiDispatcher());
        host.Open(_test.Session);
        var settings = new TestSettings();
        var scans = new ScanHost(host, settings, new App(), TimeProvider.System, NullLoggerFactory.Instance, new NoDialogs(), new InlineUiDispatcher());
        using var vm = new DashboardViewModel(host, scans, new DashboardQueries(overrides, NullLogger<DashboardQueries>.Instance), settings,
            new FileBrowserNavigator(), NullLogger<DashboardViewModel>.Instance, new InlineUiDispatcher(), _test.Time);
        await WaitUntil(() => vm.DuplicateNote == "calculating…");
        await Started();

        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction(); // e.g. new media added
            transaction.Commit();
        }

        await WaitUntil(() => vm.DuplicateNote == "paused while the inventory changes…");

        File.Delete(slow); // the real query from now on
        _test.Time.Advance(DashboardViewModel.RetryAfterInterrupt);
        await WaitUntil(() => vm.UniqueFiles == "1");
        await scans.ShutdownAsync();
        host.Close();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

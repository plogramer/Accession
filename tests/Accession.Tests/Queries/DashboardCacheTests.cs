using Accession.Core.Runtime;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Queries;

/// <summary>
/// Duplicates and files by year read every file (tens of seconds on millions of files), so they are saved on this
/// computer per inventory and media selection, and used while the inventory's data is unchanged.
/// </summary>
public sealed class DashboardCacheTests : IAsyncDisposable
{
    private static readonly Guid Inventory = Guid.Parse("7d3f2a8e-1b4c-4f5d-9e6a-0c1b2d3e4f5a");

    private readonly TestSession _test = new();
    private readonly DashboardQueries _queries = new(null, NullLogger<DashboardQueries>.Instance);
    private readonly DashboardCache _cache;
    private readonly long _m1;

    public DashboardCacheTests()
    {
        _cache = new DashboardCache(_test.Temp.Combine("cache"), NullLogger<DashboardCache>.Instance);
        using var scope = _test.Session.Database.Open();
        var media = new MediaRepository(scope);
        _m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
        var folder = ScanRows.AddFolder(scope, _m1, @"\M1\");
        ScanRows.AddFile(scope, _m1, folder, "a.txt", 10, sha1: new string('a', 40));
        ScanRows.AddFile(scope, _m1, folder, "b.txt", 10, sha1: new string('a', 40));
    }

    public ValueTask DisposeAsync()
    {
        _test.Dispose();
        return ValueTask.CompletedTask;
    }

    private static SlowDashboard Sample(long unique) => new(
        new DuplicateSummary { HashedFiles = 100, UniqueFiles = unique, HashedBytes = 1_000, UniqueBytes = 600 },
        [new MediaDuplicates { MediaKey = 1, HashedFiles = 100, UniqueFiles = unique }],
        [new YearTotal { Year = "2021", FileCount = 7, TotalBytes = 70 }]);

    private string Version() => DashboardCache.DataVersion(_test.Session.Database, _queries);

    [Fact]
    public void Saved_sections_come_back_per_media_selection_and_data_version()
    {
        var version = Version();
        _cache.Put(Inventory, version, DashboardFilter.All, Sample(60));
        _cache.Put(Inventory, version, new DashboardFilter([3, 1]), Sample(40));

        Assert.Equal(60, _cache.Get(Inventory, version, DashboardFilter.All)!.Duplicates.UniqueFiles);
        Assert.Equal(40, _cache.Get(Inventory, version, new DashboardFilter([1, 3]))!.PerMedia[0].UniqueFiles); // order does not matter
        Assert.Equal("2021", _cache.Get(Inventory, version, DashboardFilter.All)!.Years[0].Year);
        Assert.Null(_cache.Get(Inventory, version, new DashboardFilter([1])));
        Assert.Null(_cache.Get(Inventory, "another version", DashboardFilter.All));
        Assert.Null(_cache.Get(Guid.NewGuid(), version, DashboardFilter.All));

        _cache.Put(Inventory, "new version", DashboardFilter.All, Sample(10)); // the data changed: older entries go
        Assert.Null(_cache.Get(Inventory, "new version", new DashboardFilter([1, 3])));
    }

    [Fact]
    public void The_data_version_changes_when_scans_or_media_change_the_files()
    {
        var first = Version();
        Assert.Equal(first, Version()); // reading does not change it

        using (var scope = _test.Session.Database.Open())
        {
            scope.Connection.Execute("UPDATE Media SET HashedCount = 2, ScanCount = 1, LastScanCompletedUtc = '2026-09-28T10:00:00.0000000Z'");
        }

        var afterScan = Version();
        Assert.NotEqual(first, afterScan);

        using (var scope = _test.Session.Database.Open())
        {
            scope.Connection.Execute("UPDATE Media SET IsDeleted = 1");
        }

        Assert.NotEqual(afterScan, Version());
    }

    [Fact]
    public void A_damaged_cache_file_is_ignored_and_no_directory_means_no_cache()
    {
        var version = Version();
        _cache.Put(Inventory, version, DashboardFilter.All, Sample(60));
        File.WriteAllText(Directory.GetFiles(_test.Temp.Combine("cache"))[0], "{ not json");

        Assert.Null(_cache.Get(Inventory, version, DashboardFilter.All));

        var none = new DashboardCache(null, NullLogger<DashboardCache>.Instance);
        none.Put(Inventory, version, DashboardFilter.All, Sample(60));
        Assert.Null(none.Get(Inventory, version, DashboardFilter.All));
    }

    [Fact]
    public async Task The_dashboard_uses_saved_sections_and_recalculates_after_a_change()
    {
        var host = new InventoryHost(new InlineUiDispatcher());
        host.Open(_test.Session);
        var settings = new TestSettings();
        var scans = new ScanHost(host, settings, new App(), TimeProvider.System, NullLoggerFactory.Instance, new NoDialogs(), new InlineUiDispatcher());
        var inventory = _test.Session.Config.InventoryGuid;

        // What a first open calculated and saved (here: made-up numbers, to show they are used).
        _cache.Put(inventory, Version(), DashboardFilter.All, Sample(60));
        var vm = new DashboardViewModel(host, scans, _queries, settings, new FileBrowserNavigator(), NullLogger<DashboardViewModel>.Instance,
            new InlineUiDispatcher(), _test.Time, _cache);
        await vm.LastLoad;
        Assert.Equal("60", vm.UniqueFiles);
        Assert.Equal("40", vm.DuplicateFiles);

        // A scan changed the data: the real numbers are calculated (2 hashed files, 1 unique) and saved.
        using (var scope = _test.Session.Database.Open())
        {
            scope.Connection.Execute("UPDATE Media SET ScanCount = 1, HashedCount = 2");
        }

        var again = new DashboardViewModel(host, scans, _queries, settings, new FileBrowserNavigator(), NullLogger<DashboardViewModel>.Instance,
            new InlineUiDispatcher(), _test.Time, _cache);
        await again.LastLoad;
        Assert.Equal("1", again.UniqueFiles);
        Assert.Equal(1, _cache.Get(inventory, Version(), DashboardFilter.All)!.Duplicates.UniqueFiles);

        vm.Dispose();
        again.Dispose();
        await scans.ShutdownAsync();
        host.Close();
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

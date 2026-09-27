using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Scanning;
using Accession.Data.MediaManagement;
using Accession.Data.Queries;
using Accession.Data.Scanning;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Queries;

/// <summary>Runs every dashboard query against two scanned media with known content.</summary>
public sealed class DashboardQueriesTests : IAsyncLifetime
{
    private readonly TestSession _test = new();
    private readonly DashboardQueries _queries = new(null, NullLogger<DashboardQueries>.Instance);
    private long _m1;
    private long _m2;

    public async ValueTask InitializeAsync()
    {
        // M1: a.msg + copy.msg (same content), b.pdf, README (no ext), c.zzz (unknown ext)
        Write("M1", "a.msg", "same", 2019);
        Write("M1", "sub/copy.msg", "same", 2020);
        Write("M1", "b.pdf", "pdf-content", 2020);
        Write("M1", "README", "readme", 2021);
        Write("M1", "c.zzz", "zz", 2021);
        // M2: dup.msg has the same content as M1's a.msg; big.pst is the largest file
        Write("M2", "dup.msg", "same", 2019);
        Write("M2", "big.pst", new string('x', 10_000), 2022);

        var media = new MediaService(_test.Time);
        _m1 = media.Add(_test.Session, [Path.Combine(_test.Root, "M1")]).Added[0].MediaKey;
        _m2 = media.Add(_test.Session, [Path.Combine(_test.Root, "M2")]).Added[0].MediaKey;

        await using var coordinator = new ScanCoordinator(_test.Session, new FileSystemDirectoryLister(), new Sha1FileHasher(),
            () => ScanOptions.Default, new App(), TimeProvider.System, NullLogger<ScanCoordinator>.Instance);
        coordinator.Enqueue(_m1, ScanType.Full);
        coordinator.Enqueue(_m2, ScanType.Full);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (coordinator.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    public ValueTask DisposeAsync()
    {
        _test.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    private void Write(string media, string relative, string content, int year)
    {
        var path = Path.Combine([_test.Root, media, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, new DateTime(year, 6, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    private DashboardFilter Only(params long[] keys) => new(keys);

    [Fact]
    public void Summary_tiles()
    {
        var all = _queries.Summary(_test.Session.Database, DashboardFilter.All);
        Assert.Equal(2, all.MediaCount);
        Assert.Equal(3, all.FolderCount); // M1, M1\sub, M2
        Assert.Equal(7, all.FileCount);
        Assert.Equal(4 + 4 + 11 + 6 + 2 + 4 + 10_000, all.TotalBytes);
        Assert.Equal(7, all.HashedCount);
        Assert.Equal(0, all.ErrorCount);

        var m2 = _queries.Summary(_test.Session.Database, Only(_m2));
        Assert.Equal(1, m2.MediaCount);
        Assert.Equal(2, m2.FileCount);
    }

    [Fact]
    public void By_media()
    {
        var rows = _queries.ByMedia(_test.Session.Database, DashboardFilter.All);

        Assert.Equal(["M1", "M2"], rows.Select(r => r.MediaId));
        Assert.Equal(5, rows[0].FileCount);
        Assert.Equal(MediaStatus.Completed, rows[0].Status);
        Assert.Equal(1, rows[0].ScanCount);
        Assert.NotNull(rows[0].LastScanCompletedUtc);
    }

    [Fact]
    public void By_category_includes_every_category_and_totals_match()
    {
        var rows = _queries.ByCategory(_test.Session.Database, DashboardFilter.All);

        Assert.Equal(20, rows.Count);
        Assert.Equal(4, rows.Single(r => r.Category == "Email").FileCount); // a.msg, copy.msg, dup.msg, big.pst
        Assert.Equal(1, rows.Single(r => r.Category == "No Extension").FileCount);
        Assert.Equal(1, rows.Single(r => r.Category == "Other / Unknown").FileCount);
        Assert.Equal(1, rows.Single(r => r.Category == "PDF & Fixed Layout").FileCount);
        Assert.Equal(0, rows.Single(r => r.Category == "Chat").FileCount);
        Assert.Equal(7, rows.Sum(r => r.FileCount));
        Assert.Equal("Email", rows[0].Category); // ordered by size: big.pst makes Email the largest
    }

    [Fact]
    public void By_extension()
    {
        var rows = _queries.ByExtension(_test.Session.Database, DashboardFilter.All);

        var msg = rows.Single(r => r.Extension == "msg");
        Assert.Equal(3, msg.FileCount);
        Assert.Equal(12, msg.TotalBytes);
        Assert.Equal("Email", msg.Category);
        Assert.Equal("No Extension", rows.Single(r => r.Extension == "").Category);
        Assert.Equal("Other / Unknown", rows.Single(r => r.Extension == "zzz").Category);
        Assert.Equal("pst", rows[0].Extension); // largest first
    }

    [Fact]
    public void Duplicates_across_and_within_media()
    {
        var all = _queries.Duplicates(_test.Session.Database, DashboardFilter.All);
        Assert.Equal(7, all.HashedFiles);
        Assert.Equal(5, all.UniqueFiles);   // "same" x3 counts once
        Assert.Equal(2, all.DuplicateFiles);
        Assert.Equal(8, all.DuplicateBytes); // two extra copies of 4 bytes
        Assert.Equal(0, all.NotHashedFiles);

        var m1 = _queries.Duplicates(_test.Session.Database, Only(_m1));
        Assert.Equal(1, m1.DuplicateFiles);

        var byMedia = _queries.DuplicatesByMedia(_test.Session.Database, DashboardFilter.All).ToDictionary(r => r.MediaKey);
        Assert.Equal(1, byMedia[_m1].DuplicateFiles);
        Assert.Equal(0, byMedia[_m2].DuplicateFiles);
    }

    [Fact]
    public void By_year()
    {
        var rows = _queries.ByYear(_test.Session.Database, DashboardFilter.All);

        Assert.Equal(["2019", "2020", "2021", "2022"], rows.Select(r => r.Year));
        Assert.Equal([2L, 2L, 2L, 1L], rows.Select(r => r.FileCount));
    }

    [Fact]
    public void Largest_files()
    {
        var rows = _queries.LargestFiles(_test.Session.Database, DashboardFilter.All);

        Assert.Equal(7, rows.Count);
        Assert.Equal(@"\M2\big.pst", rows[0].RelativePath);
        Assert.Equal("M2", rows[0].MediaId);
        Assert.Equal(10_000, rows[0].SizeBytes);
        Assert.Equal(new DateTimeOffset(2022, 6, 1, 12, 0, 0, TimeSpan.Zero), rows[0].ModifiedUtc);
        Assert.Empty(_queries.LargestFiles(_test.Session.Database, Only(9999)));
    }

    [Fact]
    public void Deleted_media_are_excluded()
    {
        new MediaService(_test.Time).Delete(_test.Session, _m2);

        Assert.Equal(1, _queries.Summary(_test.Session.Database, DashboardFilter.All).MediaCount);
        Assert.Equal(5, _queries.ByCategory(_test.Session.Database, DashboardFilter.All).Sum(r => r.FileCount));
    }

    [Fact]
    public void Override_file_replaces_the_embedded_query()
    {
        var dir = _test.Temp.Combine("Overrides");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SummaryTiles.sql"),
            "SELECT 42 AS MediaCount, 0 AS FolderCount, 0 AS FileCount, 0 AS TotalBytes, 0 AS HashedCount, 0 AS ErrorCount WHERE @MediaKeysJson IS NULL OR 1 = 1");
        var queries = new DashboardQueries(dir, NullLogger<DashboardQueries>.Instance);

        Assert.True(queries.Runner.IsOverridden("SummaryTiles"));
        Assert.False(queries.Runner.IsOverridden("ByMedia"));
        Assert.Equal(42, queries.Summary(_test.Session.Database, DashboardFilter.All).MediaCount);
        Assert.Equal(2, queries.ByMedia(_test.Session.Database, DashboardFilter.All).Count);
    }
}

using Accession.Data.Browsing;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;

namespace Accession.Tests.Browsing;

/// <summary>Numbered pages must match a straight read of the whole sorted result, whatever query reads them.</summary>
public sealed class FilePagerTests : IDisposable
{
    private const int FileCount = 57;
    private const int PageSize = 10;
    private readonly TestInventory _inventory = new();
    private readonly FileBrowserQueries _queries = new();

    public FilePagerTests()
    {
        using var scope = _inventory.Database.Open();
        var media = new MediaRepository(scope).Insert("M1", @"\M1\", TestInventory.CreatedAt, "u");
        var root = ScanRows.AddFolder(scope, media, @"\M1\");
        var sub = ScanRows.AddFolder(scope, media, @"\M1\sub\", root);
        for (var i = 0; i < FileCount; i++)
        {
            // Sizes repeat (ties) so the FileId tiebreak is exercised; names are not in FileId order.
            ScanRows.AddFile(scope, media, i % 3 == 0 ? sub : root, $"file_{(i * 37) % FileCount:000}.txt", size: (i % 7) * 100);
        }
    }

    public void Dispose() => _inventory.Dispose();

    public static TheoryData<FileSortColumn, bool> Sorts => new()
    {
        { FileSortColumn.Default, false },
        { FileSortColumn.Name, false },
        { FileSortColumn.Size, true },
        { FileSortColumn.Size, false },
        { FileSortColumn.Modified, true },
    };

    [Theory]
    [MemberData(nameof(Sorts))]
    public void Next_pages_match_the_full_list(FileSortColumn sort, bool descending)
    {
        var all = All(sort, descending);
        var pager = Pager(sort, descending);

        for (var page = 0; page < pager.PageCount; page++)
        {
            Assert.Equal(Slice(all, page), Ids(pager.Load(page, TestContext.Current.CancellationToken)));
            Assert.Equal(FilePageMethod.After, pager.LastMethod);
        }
    }

    [Theory]
    [MemberData(nameof(Sorts))]
    public void Going_to_the_last_page_reads_from_the_end(FileSortColumn sort, bool descending)
    {
        var all = All(sort, descending);
        var pager = Pager(sort, descending);

        var items = pager.Load(pager.PageCount - 1, TestContext.Current.CancellationToken);

        Assert.Equal(FilePageMethod.Last, pager.LastMethod);
        Assert.Equal(7, items.Count);
        Assert.Equal(Slice(all, 5), Ids(items));
    }

    [Theory]
    [MemberData(nameof(Sorts))]
    public void Previous_page_after_a_jump_uses_keyset_before(FileSortColumn sort, bool descending)
    {
        var all = All(sort, descending);
        var pager = Pager(sort, descending);
        pager.Load(3, TestContext.Current.CancellationToken);
        Assert.Equal(FilePageMethod.Offset, pager.LastMethod);

        var items = pager.Load(2, TestContext.Current.CancellationToken);

        Assert.Equal(FilePageMethod.Before, pager.LastMethod);
        Assert.Equal(Slice(all, 2), Ids(items));
    }

    [Theory]
    [MemberData(nameof(Sorts))]
    public void Jump_uses_offset_then_next_continues_with_keyset(FileSortColumn sort, bool descending)
    {
        var all = All(sort, descending);
        var pager = Pager(sort, descending);

        Assert.Equal(Slice(all, 4), Ids(pager.Load(4, TestContext.Current.CancellationToken)));
        Assert.Equal(FilePageMethod.Offset, pager.LastMethod);
        Assert.Equal(Slice(all, 5), Ids(pager.Load(5, TestContext.Current.CancellationToken)));
        Assert.Equal(FilePageMethod.After, pager.LastMethod);
    }

    [Fact]
    public void Visited_pages_are_read_again_by_keyset()
    {
        var pager = Pager(FileSortColumn.Name, false);
        pager.Load(0, TestContext.Current.CancellationToken);
        pager.Load(1, TestContext.Current.CancellationToken);
        pager.Load(2, TestContext.Current.CancellationToken);

        pager.Load(1, TestContext.Current.CancellationToken);

        Assert.Equal(FilePageMethod.After, pager.LastMethod);
    }

    [Fact]
    public void Totals_page_count_and_clamping()
    {
        var pager = Pager(FileSortColumn.Default, false);

        Assert.Equal(FileCount, pager.TotalCount);
        Assert.Equal(6, pager.PageCount);
        pager.Load(99, TestContext.Current.CancellationToken);
        Assert.Equal(5, pager.PageIndex);
        pager.Load(-3, TestContext.Current.CancellationToken);
        Assert.Equal(0, pager.PageIndex);
    }

    [Fact]
    public void Filtered_pages_only_contain_matching_files()
    {
        var pager = new FilePager(_queries, _inventory.Database, new FileFilter { MinSize = 500 }, FileSortColumn.Size, false, PageSize);
        pager.CountTotals(TestContext.Current.CancellationToken);

        var items = Enumerable.Range(0, pager.PageCount).SelectMany(p => pager.Load(p, TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(pager.TotalCount, items.Count);
        Assert.All(items, f => Assert.True(f.SizeBytes >= 500));
    }

    private FilePager Pager(FileSortColumn sort, bool descending)
    {
        var pager = new FilePager(_queries, _inventory.Database, FileFilter.None, sort, descending, PageSize);
        pager.CountTotals(TestContext.Current.CancellationToken);
        return pager;
    }

    private List<long> All(FileSortColumn sort, bool descending) =>
        Ids(_queries.Page(_inventory.Database, FileFilter.None, sort, descending, after: null, pageSize: 10_000).Items);

    private static List<long> Slice(List<long> all, int page) => all.Skip(page * PageSize).Take(PageSize).ToList();

    private static List<long> Ids(IEnumerable<FileItem> items) => items.Select(f => f.FileId).ToList();
}

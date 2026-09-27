using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Scanning;
using Accession.Data.Browsing;
using Accession.Data.MediaManagement;
using Accession.Data.Schema;
using Accession.Data.Scanning;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Browsing;

public sealed class FileBrowserQueriesTests : IAsyncLifetime
{
    private readonly TestSession _test = new();
    private readonly FileBrowserQueries _queries = new();
    private long _m1;
    private long _m2;

    public async ValueTask InitializeAsync()
    {
        Write("M1", "Budget.xlsx", new string('b', 300), 2021);
        Write("M1", "mail/a.msg", "same", 2019);
        Write("M1", "mail/B.msg", "other", 2020);
        Write("M1", "mail/2019/c.pst", new string('p', 5000), 2019);
        Write("M1", "mail_x/under_score.txt", "u", 2022);
        Write("M1", "README", "readme", 2023);
        Write("M2", "copy.msg", "same", 2019);
        Write("M2", "notes 100%.txt", "percent", 2024);

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
        File.SetLastWriteTimeUtc(path, new DateTime(year, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private List<FileItem> All(FileFilter filter, FileSortColumn sort = FileSortColumn.Default, bool descending = false, int pageSize = 500)
    {
        var items = new List<FileItem>();
        FilePageCursor? cursor = null;
        do
        {
            var page = _queries.Page(_test.Session.Database, filter, sort, descending, cursor, pageSize);
            items.AddRange(page.Items);
            cursor = page.Next;
        }
        while (cursor is not null);

        return items;
    }

    private IReadOnlyList<string> Paths(FileFilter filter) => All(filter).Select(f => f.RelativePath).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Folder_tree_is_loaded_level_by_level()
    {
        var roots = _queries.MediaRoots(_test.Session.Database);
        Assert.Equal(["M1", "M2"], roots.Select(r => r.Name));
        Assert.True(roots[0].HasChildren);
        Assert.False(roots[1].HasChildren);

        var children = _queries.ChildFolders(_test.Session.Database, roots[0].FolderId);
        Assert.Equal(["mail", "mail_x"], children.Select(c => c.Name));
        Assert.True(children[0].HasChildren);
        Assert.Equal(@"\M1\mail\", children[0].RelativePath);
    }

    [Fact]
    public void Rows_have_metadata_category_and_duplicate_count()
    {
        var a = All(FileFilter.None).Single(f => f.Name == "a.msg");

        Assert.Equal("M1", a.MediaId);
        Assert.Equal(@"\M1\mail\a.msg", a.RelativePath);
        Assert.Equal("Email", a.Category);
        Assert.Equal(4, a.SizeBytes);
        Assert.Equal(HashStatus.Hashed, a.HashStatus);
        Assert.Equal(2, a.DuplicateCount); // copy.msg in M2
        Assert.Equal(new DateTimeOffset(2019, 3, 1, 0, 0, 0, TimeSpan.Zero), a.ModifiedUtc);
        Assert.Equal("No Extension", All(FileFilter.None).Single(f => f.Name == "README").Category);
    }

    [Theory]
    [InlineData(FileSortColumn.Default, false)]
    [InlineData(FileSortColumn.Name, false)]
    [InlineData(FileSortColumn.Name, true)]
    [InlineData(FileSortColumn.Extension, false)]
    [InlineData(FileSortColumn.Size, false)]
    [InlineData(FileSortColumn.Size, true)]
    [InlineData(FileSortColumn.Modified, false)]
    [InlineData(FileSortColumn.Modified, true)]
    public void Keyset_paging_returns_every_file_once_in_order(FileSortColumn sort, bool descending)
    {
        var paged = All(FileFilter.None, sort, descending, pageSize: 2);
        var single = All(FileFilter.None, sort, descending, pageSize: 500);

        Assert.Equal(8, paged.Count);
        Assert.Equal(single.Select(f => f.FileId), paged.Select(f => f.FileId));
        Assert.Equal(8, paged.Select(f => f.FileId).Distinct().Count());
        if (sort == FileSortColumn.Size)
        {
            var sizes = paged.Select(f => f.SizeBytes).ToList();
            Assert.Equal(descending ? sizes.OrderDescending() : sizes.Order(), sizes);
        }
    }

    [Fact]
    public void Media_and_folder_filters()
    {
        Assert.Equal(2, All(new FileFilter { MediaKey = _m2 }).Count);

        var mail = _queries.ChildFolders(_test.Session.Database, _queries.MediaRoots(_test.Session.Database)[0].FolderId)[0];
        Assert.Equal([@"\M1\mail\2019\c.pst", @"\M1\mail\B.msg", @"\M1\mail\a.msg"], Paths(new FileFilter { FolderId = mail.FolderId }));
        Assert.Equal([@"\M1\mail\B.msg", @"\M1\mail\a.msg"], Paths(new FileFilter { FolderId = mail.FolderId, IncludeSubfolders = false }));
    }

    [Fact]
    public void Folder_prefix_does_not_match_sibling_folders_with_underscore()
    {
        // "\M1\mail\" must not match "\M1\mail_x\" (underscore is a LIKE wildcard and must be escaped).
        var mail = _queries.ChildFolders(_test.Session.Database, _queries.MediaRoots(_test.Session.Database)[0].FolderId)[0];

        Assert.DoesNotContain(@"\M1\mail_x\under_score.txt", Paths(new FileFilter { FolderId = mail.FolderId }));
    }

    [Fact]
    public void Category_extension_size_date_and_name_filters()
    {
        Assert.Equal(4, All(new FileFilter { CategoryId = 1 }).Count); // Email: a, B, c.pst, copy
        Assert.Equal(3, All(new FileFilter { Extension = ".MSG" }).Count);
        Assert.Equal([@"\M1\README"], Paths(new FileFilter { Extension = "" }));
        Assert.Equal(2, All(new FileFilter { MinSize = 300 }).Count);
        Assert.Equal([@"\M1\Budget.xlsx"], Paths(new FileFilter { MinSize = 300, MaxSize = 300 }));
        Assert.Equal(3, All(new FileFilter
        {
            ModifiedFrom = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ModifiedTo = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        }).Count);
        Assert.Equal([@"\M1\mail\B.msg"], Paths(new FileFilter { NameContains = "b.m" }));
        Assert.Equal([@"\M2\notes 100%.txt"], Paths(new FileFilter { NameContains = "100%" }));
        Assert.Equal([@"\M1\mail_x\under_score.txt"], Paths(new FileFilter { NameContains = "r_s" }));
    }

    [Fact]
    public void Duplicate_sha1_hash_status_and_error_filters()
    {
        Assert.Equal([@"\M1\mail\a.msg", @"\M2\copy.msg"], Paths(new FileFilter { DuplicatesOnly = true }));
        var sha1 = All(FileFilter.None).Single(f => f.Name == "a.msg").Sha1!;
        Assert.Equal(2, All(new FileFilter { Sha1 = sha1.ToUpperInvariant() }).Count);
        Assert.Equal(8, All(new FileFilter { HashStatus = HashStatus.Hashed }).Count);
        Assert.Empty(All(new FileFilter { ErrorsOnly = true }));
    }

    [Fact]
    public void Totals_follow_the_filter()
    {
        var all = _queries.Totals(_test.Session.Database, FileFilter.None);
        Assert.Equal(8, all.FileCount);
        Assert.Equal(300 + 4 + 5 + 5000 + 1 + 6 + 4 + 7, all.TotalBytes);

        var email = _queries.Totals(_test.Session.Database, new FileFilter { CategoryId = 1 });
        Assert.Equal(4, email.FileCount);
    }

    [Fact]
    public void Categories_screen_counts_and_extensions()
    {
        var queries = new CategoryQueries();
        var categories = queries.Categories(_test.Session.Database);

        Assert.Equal(20, categories.Count);
        Assert.Equal(4, categories.Single(c => c.Name == "Email").FileCount);
        Assert.Equal(1, categories.Single(c => c.CategoryId == CategoryCatalog.NoExtensionId).FileCount);

        var email = queries.Extensions(_test.Session.Database, 1);
        Assert.Equal("msg", email[0].Extension);
        Assert.Equal(3, email[0].FileCount);
        Assert.Contains(email, e => e.Extension == "eml" && e.FileCount == 0 && e.IsMapped);

        var none = queries.Extensions(_test.Session.Database, CategoryCatalog.NoExtensionId);
        var readme = Assert.Single(none);
        Assert.Equal("", readme.Extension);
        Assert.False(readme.IsMapped);
    }
}

using Accession.Core.Model;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using Accession.Data.SavedSearches;
using Accession.Data.Sessions;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.SavedSearches;

/// <summary>Saved searches: fixed lists of files, by filter or selection, that survive rescans; audited, read-only safe.</summary>
public sealed class SavedSearchServiceTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly SavedSearchService _service;
    private readonly FileBrowserQueries _files = new();
    private readonly long _m1;
    private readonly long _m2;

    public SavedSearchServiceTests()
    {
        _service = new SavedSearchService(_test.Factory);
        using var scope = _test.Session.Database.Open();
        using var transaction = scope.BeginTransaction();
        var media = new MediaRepository(scope);
        _m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
        _m2 = media.Insert("M2", @"\M2\", _test.Time.GetUtcNow(), "u");
        AddFiles(scope, _m1, @"\M1\", 100, "msg");
        AddFiles(scope, _m2, @"\M2\", 50, "pdf");
        transaction.Commit();
    }

    public void Dispose() => _test.Dispose();

    private static void AddFiles(Accession.Data.DbScope scope, long media, string folderPath, int count, string extension)
    {
        var folder = ScanRows.AddFolder(scope, media, folderPath);
        for (var i = 0; i < count; i++)
        {
            ScanRows.AddFile(scope, media, folder, $"file{i:000}.{extension}", size: 1_000);
        }
    }

    private long Count(FileFilter filter) => _files.Totals(_test.Session.Database, filter).FileCount;

    [Fact]
    public void Create_list_edit_and_delete_are_audited()
    {
        var id = _service.Create(_test.Session, "  Privileged  ", "Attorney emails");

        var info = Assert.Single(_service.List(_test.Session.Database));
        Assert.Equal(("Privileged", "Attorney emails", 0L, 0L), (info.Name, info.Description, info.FileCount, info.TotalBytes));
        Assert.Equal(@"CORP\jdoe", info.CreatedBy);

        _service.Update(_test.Session, id, "Privileged (reviewed)", " ");
        info = Assert.Single(_service.List(_test.Session.Database));
        Assert.Equal("Privileged (reviewed)", info.Name);
        Assert.Null(info.Description);

        _service.Delete(_test.Session, id);
        Assert.Empty(_service.List(_test.Session.Database));

        Assert.Equal([AuditAction.SavedSearchCreated, AuditAction.SavedSearchChanged, AuditAction.SavedSearchDeleted],
            _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.SavedSearchCreated, AuditAction.SavedSearchChanged, AuditAction.SavedSearchDeleted] })
                .Select(a => a.Action).Reverse());
    }

    [Fact]
    public void Names_are_required_and_unique_ignoring_case()
    {
        Assert.Equal("Enter a name.", SavedSearchService.ValidateName("  "));
        Assert.StartsWith("Use at most", SavedSearchService.ValidateName(new string('x', 101)), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => _service.Create(_test.Session, "", null));

        var first = _service.Create(_test.Session, "Hot docs", null);
        Assert.Throws<SavedSearchNameTakenException>(() => _service.Create(_test.Session, "HOT DOCS", null));
        var second = _service.Create(_test.Session, "Other", null);
        Assert.Throws<SavedSearchNameTakenException>(() => _service.Update(_test.Session, second, "hot docs", null));
        _service.Update(_test.Session, first, "Hot Docs", "renaming to itself in another case is fine");
    }

    [Fact]
    public void All_results_of_a_filter_are_added_once()
    {
        var id = _service.Create(_test.Session, "Emails", null);

        Assert.Equal(100, _service.AddFiles(_test.Session, id, new FileFilter { Extension = "msg" }));
        Assert.Equal(0, _service.AddFiles(_test.Session, id, new FileFilter { Extension = "msg" })); // already in the list
        Assert.Equal(50, _service.AddFiles(_test.Session, id, new FileFilter { MediaKey = _m2 }));

        var info = Assert.Single(_service.List(_test.Session.Database));
        Assert.Equal((150L, 150_000L), (info.FileCount, info.TotalBytes));
        Assert.Equal(150, Count(new FileFilter { SavedSearchId = id }));
        Assert.Equal(50, Count(new FileFilter { SavedSearchId = id, Extension = "pdf" })); // filters narrow within it
        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.SavedSearchFilesAdded] }).First();
        Assert.Contains("\"added\":50", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_rows_are_added_and_removed()
    {
        var id = _service.Create(_test.Session, "Picked", null);
        var page = _files.Page(_test.Session.Database, new FileFilter { MediaKey = _m1 }, FileSortColumn.Default, false, null, 5);
        var picked = page.Items.Select(f => f.FileId).ToList();

        Assert.Equal(5, _service.AddFiles(_test.Session, id, new FileFilter { FileIds = picked }));
        Assert.Equal(2, _service.RemoveFiles(_test.Session, id, new FileFilter { FileIds = picked.Take(2).ToList() }));
        Assert.Equal(3, Count(new FileFilter { SavedSearchId = id }));

        Assert.Equal(3, _service.RemoveFiles(_test.Session, id, new FileFilter { SavedSearchId = id })); // "Remove all results"
        Assert.Equal(0, Count(new FileFilter { SavedSearchId = id }));
    }

    [Fact]
    public void Files_stay_in_the_list_after_a_rescan_replaces_their_rows()
    {
        var id = _service.Create(_test.Session, "Survives rescan", null);
        _service.AddFiles(_test.Session, id, new FileFilter { MediaKey = _m2 });

        using (var scope = _test.Session.Database.Open())
        {
            // What a rescan does: all rows of the media are replaced; one file is gone from disk now.
            using var transaction = scope.BeginTransaction();
            new ScanDataRepository(scope).DeleteForMedia(_m2);
            var folder = ScanRows.AddFolder(scope, _m2, @"\M2\");
            for (var i = 1; i < 50; i++)
            {
                ScanRows.AddFile(scope, _m2, folder, $"FILE{i:000}.pdf", size: 2_000); // names compare without case
            }

            transaction.Commit();
        }

        Assert.Equal(49, Count(new FileFilter { SavedSearchId = id }));
        Assert.Equal(98_000, Assert.Single(_service.List(_test.Session.Database)).TotalBytes);
    }

    [Fact]
    public void Deleting_a_media_removes_its_files_from_saved_searches()
    {
        var id = _service.Create(_test.Session, "Both media", null);
        _service.AddFiles(_test.Session, id, FileFilter.None);

        new MediaService(TimeProvider.System).Delete(_test.Session, _m1);

        using var scope = _test.Session.Database.Open();
        Assert.Equal(50, scope.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM SavedSearchFile"));
    }

    [Fact]
    public void A_read_only_inventory_can_list_but_not_change_saved_searches()
    {
        _service.Create(_test.Session, "Existing", null);
        var interaction = new ReadOnlyInteraction();
        using var readOnly = new InventoryOpenService(_test.Factory, new App(), new RootPathService(),
            NullLogger<InventoryOpenService>.Instance).Open(_test.Session.DbPath, interaction)!;

        Assert.True(readOnly.IsReadOnly);
        Assert.Single(_service.List(readOnly.Database));
        Assert.Throws<InvalidOperationException>(() => _service.Create(readOnly, "New", null));
    }

    private sealed class App : Accession.Core.Runtime.IAppInfo
    {
        public string Version => "0.1.0";
    }

    private sealed class ReadOnlyInteraction : IOpenInteraction
    {
        public bool ConfirmUpgrade(int fromVersion, int toVersion) => false;

        public LockConflictChoice ResolveLockConflict(LockConflict conflict) => LockConflictChoice.OpenReadOnly;

        public RootUnreachableResolution ResolveRootUnreachable(string rootPath) => new(RootUnreachableChoice.ContinueOffline, null);
    }
}

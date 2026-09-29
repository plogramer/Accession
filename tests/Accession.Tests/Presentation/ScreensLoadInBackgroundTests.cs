using System.Diagnostics;
using Accession.Core.Runtime;
using Accession.Core.Threading;
using Accession.Data;
using Accession.Data.Browsing;
using Accession.Data.MediaManagement;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.Presentation.ViewModels.MediaScreen;
using Accession.Presentation.ViewModels.Scanning;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>
/// The screens are built on the UI thread when an inventory opens. They read the database in the background: a read
/// on the UI thread waits for any write in progress (rollback journal, up to the 30 s busy timeout), which froze the
/// app, the progress spinner included, while a large inventory opened.
/// </summary>
public sealed class ScreensLoadInBackgroundTests : IAsyncDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly ScanHost _scans;

    public ScreensLoadInBackgroundTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope).Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            var folder = ScanRows.AddFolder(scope, media, @"\M1\");
            ScanRows.AddFile(scope, media, folder, "a.txt", 10);
            transaction.Commit();
        }

        _host.Open(_test.Session);
        _scans = new ScanHost(_host, _settings, new App(), TimeProvider.System, NullLoggerFactory.Instance, new NoDialogs(), new InlineUiDispatcher());
    }

    public async ValueTask DisposeAsync()
    {
        await _scans.ShutdownAsync();
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public async Task Building_the_screens_does_not_wait_for_a_write_in_progress()
    {
        var watch = Stopwatch.StartNew();
        DashboardViewModel dashboard;
        WebFilesViewModel files;
        ErrorsViewModel errors;
        AuditLogViewModel audit;
        MediaListViewModel media;
        CategoriesViewModel categories;
        using (var writer = SqliteConnectionFactory.Open(_test.Session.DbPath))
        {
            writer.Execute("BEGIN EXCLUSIVE"); // no one can read until it ends

            dashboard = new DashboardViewModel(_host, _scans, new DashboardQueries(null, NullLogger<DashboardQueries>.Instance), _settings,
                new FileBrowserNavigator(), NullLogger<DashboardViewModel>.Instance, new InlineUiDispatcher(), TimeProvider.System);
            files = new WebFilesViewModel(_host, new FileBrowserQueries(), new CategoryQueries(), _settings, new RecordingDesktop(), new NoDialogs(),
                new ToastService(TimeProvider.System), NullLogger<WebFilesViewModel>.Instance);
            errors = new ErrorsViewModel(_host, _scans, _settings, new NoDialogs(), NullLogger<ErrorsViewModel>.Instance, new RecordingDesktop(),
                new ToastService(TimeProvider.System));
            audit = new AuditLogViewModel(_host, _settings, NullLogger<AuditLogViewModel>.Instance);
            var workflows = new MediaWorkflows(_host, new NoDialogs(), new BusyTracker(TimeProvider.System), new MediaDiscoveryService(),
                new MediaService(TimeProvider.System), _settings, _scans, NullLogger<MediaWorkflows>.Instance, new RecordingDesktop());
            media = new MediaListViewModel(_host, workflows, _scans, _settings, NullLogger<MediaListViewModel>.Instance, new InlineUiDispatcher(),
                new FileBrowserNavigator());
            categories = new CategoriesViewModel(_host, new CategoryQueries(), _settings, new FileBrowserNavigator(), NullLogger<CategoriesViewModel>.Instance);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Building the screens waited {watch.ElapsedMilliseconds} ms.");
            await Task.Delay(200, TestContext.Current.CancellationToken);
            writer.Execute("COMMIT");
        }

        // Once the write ends, everything is there.
        await Task.WhenAll(dashboard.LastLoad, files.LastLookups, errors.LastLoad, audit.LastLoad, media.LastLoad)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(["M1"], dashboard.MediaFilter.Select(m => m.MediaId));
        Assert.Equal(["M1"], files.Folders.Select(f => f.Info.Name));
        Assert.Equal(["All media", "M1"], errors.MediaOptions.Select(o => o.Label));
        Assert.True(audit.TotalCount > 0);
        Assert.Equal(["M1"], media.Rows.Select(r => r.MediaId));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (categories.Categories.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.NotEmpty(categories.Categories);

        dashboard.Dispose();
        files.Dispose();
        errors.Dispose();
        audit.Dispose();
        media.Dispose();
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

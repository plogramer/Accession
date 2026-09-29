using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Scanning;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>Numbered pages and filters of the Errors screen.</summary>
public sealed class ErrorsViewModelTests : IAsyncDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly RecordingDesktop _desktop = new();
    private readonly ToastService _toasts = new(TimeProvider.System);
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly ScanHost _scans;
    private readonly long _m1;

    public ErrorsViewModelTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            // One transaction: thousands of single-row commits are slow on Windows (synchronous=FULL).
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            _m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            var m2 = media.Insert("M2", @"\M2\", _test.Time.GetUtcNow(), "u");
            var errors = new ScanErrorRepository(scope);
            for (var i = 0; i < 1_250; i++)
            {
                errors.Insert(new ScanErrorEntry
                {
                    MediaKey = _m1,
                    RelativePath = $@"\M1\locked\file{i:0000}.pst",
                    ItemType = ScanItemType.File,
                    ErrorType = i % 5 == 0 ? ScanErrorType.AccessDenied : ScanErrorType.FileLocked,
                    Message = "The process cannot access the file.",
                    OccurredAtUtc = _test.Time.GetUtcNow(),
                });
            }

            for (var i = 0; i < 50; i++)
            {
                errors.Insert(new ScanErrorEntry
                {
                    MediaKey = m2,
                    RelativePath = $@"\M2\link{i}\",
                    ItemType = ScanItemType.Folder,
                    ErrorType = ScanErrorType.ReparsePointSkipped,
                    Severity = ScanErrorSeverity.Info,
                    OccurredAtUtc = _test.Time.GetUtcNow(),
                });
            }

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
    public async Task First_page_and_total_exclude_info_entries()
    {
        using var vm = await Create();

        Assert.Equal(1_250, vm.TotalCount);
        Assert.Equal(500, vm.PageRows.Count);
        Assert.Equal(0, vm.PageIndex);
    }

    [Fact]
    public async Task Last_page_by_jump_and_previous_page()
    {
        using var vm = await Create();

        await vm.GoToPageAsync(2);
        Assert.Equal(250, vm.PageRows.Count);
        Assert.EndsWith("file1249.pst", vm.PageRows[^1].RelativePath, StringComparison.Ordinal);

        await vm.GoToPageAsync(1);
        Assert.EndsWith("file0500.pst", vm.PageRows[0].RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filters_by_type_media_and_info()
    {
        using var vm = await Create();

        vm.ErrorTypeFilterValue = nameof(ScanErrorType.AccessDenied);
        Assert.Equal(250, vm.TotalCount);

        vm.ErrorTypeFilterValue = string.Empty;
        vm.ShowInfo = true;
        Assert.Equal(1_300, vm.TotalCount);

        vm.MediaFilterValue = vm.MediaFilterOptions.Single(o => o.Label == "M2").Value;
        Assert.Equal(50, vm.TotalCount);
        Assert.All(vm.PageRows, r => Assert.Equal("M2", r.MediaId));
    }

    [Fact]
    public async Task Page_size_change_returns_to_the_first_page()
    {
        using var vm = await Create();
        await vm.GoToPageAsync(2);

        await vm.SetPageSizeAsync(1_000);

        Assert.Equal(0, vm.PageIndex);
        Assert.Equal(1_000, vm.PageRows.Count);
    }

    [Fact]
    public async Task Copy_path_of_the_selected_error()
    {
        using var vm = await Create();
        vm.SelectedRow = vm.PageRows[3];

        vm.CopyPathCommand.Execute(null);

        Assert.Equal(Path.Combine(_test.Root, "M1", "locked", "file0003.pst"), _desktop.Clipboard);
        Assert.Contains(_toasts.Items, t => t.Message == "Path copied");
    }

    private async Task<ErrorsViewModel> Create()
    {
        var vm = new ErrorsViewModel(_host, _scans, _settings, new NoDialogs(), NullLogger<ErrorsViewModel>.Instance, _desktop, _toasts);
        await vm.LastLoad; // read in the background
        return vm;
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

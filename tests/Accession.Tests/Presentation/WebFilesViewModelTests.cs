using Accession.Data.Browsing;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>The web Files screen's paging and filters, against a real inventory.</summary>
public sealed class WebFilesViewModelTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly RecordingDesktop _desktop = new();
    private readonly ToastService _toasts = new(TimeProvider.System);
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly long _m1;
    private readonly long _m2;
    private readonly long _subFolder;

    public WebFilesViewModelTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            var media = new MediaRepository(scope);
            _m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            _m2 = media.Insert("M2", @"\M2\", _test.Time.GetUtcNow(), "u");
            var root1 = ScanRows.AddFolder(scope, _m1, @"\M1\");
            _subFolder = ScanRows.AddFolder(scope, _m1, @"\M1\mail\", root1);
            var root2 = ScanRows.AddFolder(scope, _m2, @"\M2\");
            for (var i = 0; i < 2_500; i++)
            {
                ScanRows.AddFile(scope, _m1, i < 400 ? _subFolder : root1, $"doc_{i:0000}.{(i % 2 == 0 ? "msg" : "pdf")}", size: i * 10,
                    sha1: i < 10 ? new string('a', 40) : null);
            }

            for (var i = 0; i < 300; i++)
            {
                ScanRows.AddFile(scope, _m2, root2, $"copy_{i:000}.xlsx", size: 5);
            }
        }

        _host.Open(_test.Session);
    }

    public void Dispose()
    {
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public async Task First_page_has_1000_rows_and_the_total()
    {
        using var vm = await Create();

        Assert.Equal(1_000, vm.Rows.Count);
        Assert.Equal(0, vm.PageIndex);
        Assert.Equal(2_800, vm.TotalCount);
        Assert.StartsWith("2,800 files", vm.TotalsText, StringComparison.Ordinal);
        Assert.False(vm.IsCounting);
    }

    [Fact]
    public async Task Last_page_holds_the_remaining_rows()
    {
        using var vm = await Create();

        await vm.GoToPageAsync(2);

        Assert.Equal(2, vm.PageIndex);
        Assert.Equal(800, vm.Rows.Count);
    }

    [Fact]
    public async Task Page_size_change_is_remembered_and_returns_to_page_one()
    {
        using var vm = await Create();
        await vm.GoToPageAsync(1);

        await vm.SetPageSizeAsync(2_000);
        await Idle(vm);

        Assert.Equal(0, vm.PageIndex);
        Assert.Equal(2_000, vm.Rows.Count);
        Assert.Equal(2_000, _settings.Current.FilesPageSize);
    }

    [Fact]
    public async Task Preset_filter_from_another_screen()
    {
        using var vm = await Create();

        vm.ApplyPreset(new FileFilter { MediaKey = _m2 });
        await Idle(vm);

        Assert.Equal(300, vm.TotalCount);
        Assert.All(vm.Rows, r => Assert.Equal("M2", r.MediaId));
        Assert.Contains("Media M2", vm.ActiveFilters);
    }

    [Fact]
    public async Task Sorting_by_size_descending()
    {
        using var vm = await Create();

        await vm.SortByAsync("size");
        await vm.SortByAsync("size");
        await Idle(vm);

        Assert.True(vm.SortDescending);
        Assert.Equal("doc_2499.pdf", vm.Rows[0].Name);
    }

    [Fact]
    public async Task Folder_selection_limits_the_files()
    {
        using var vm = await Create();
        var m1 = vm.Folders.First(f => f.Info.Name == "M1");
        m1.IsExpanded = true;

        vm.SelectedFolder = m1.Children.Single(c => c.Info.Name == "mail");
        await Idle(vm);

        Assert.Equal(400, vm.TotalCount);
        Assert.Contains("In mail", vm.ActiveFilters);
    }

    [Fact]
    public async Task Invalid_size_filter_is_reported_and_not_applied()
    {
        using var vm = await Create();

        vm.MinSizeText = "lots";
        vm.ApplyCommand.Execute(null);

        Assert.Contains("number", vm.FilterError, StringComparison.Ordinal);
        Assert.Equal(2_800, vm.TotalCount);
    }

    [Fact]
    public async Task Copy_path_and_show_all_copies()
    {
        using var vm = await Create();
        vm.SelectedRow = vm.Rows.First(r => r.Sha1 is not null);

        vm.CopyPathCommand.Execute(null);
        Assert.Equal(Path.Combine(_test.Root, "M1", "mail", vm.SelectedRow.Name), _desktop.Clipboard);
        Assert.Contains(_toasts.Items, t => t.Message == "Path copied");

        vm.ShowCopiesCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(10, vm.TotalCount);
    }

    [Fact]
    public async Task Column_choice_is_remembered()
    {
        using var vm = await Create();

        vm.ToggleColumn("sha1");

        Assert.Contains("sha1", vm.VisibleColumns);
        Assert.Contains("sha1", _settings.Current.FilesColumns);
    }

    private async Task<WebFilesViewModel> Create()
    {
        var vm = new WebFilesViewModel(_host, new FileBrowserQueries(), new CategoryQueries(), _settings, _desktop, new NoDialogs(),
            _toasts, NullLogger<WebFilesViewModel>.Instance);
        await Idle(vm);
        return vm;
    }

    private static async Task Idle(WebFilesViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        while ((vm.IsLoading || vm.IsCounting) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

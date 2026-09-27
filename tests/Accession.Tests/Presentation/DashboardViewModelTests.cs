using Accession.Core.Runtime;
using Accession.Data.Browsing;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Dashboard;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>The Dashboard's media selection: the filter list, the By media table and the reload after a change.</summary>
public sealed class DashboardViewModelTests : IAsyncDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly FileBrowserNavigator _navigator = new();
    private readonly ScanHost _scans;
    private readonly long _m1;
    private readonly long _m2;

    public DashboardViewModelTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            var media = new MediaRepository(scope);
            _m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            _m2 = media.Insert("M2", @"\M2\", _test.Time.GetUtcNow(), "u");
            media.Insert("M10", @"\M10\", _test.Time.GetUtcNow(), "u");
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
    public async Task Everything_is_selected_at_first()
    {
        using var vm = await Create();

        Assert.True(vm.AllMediaSelected);
        Assert.Equal("All media", vm.FilterText);
        Assert.Equal("3 of 3 selected", vm.SelectionSummary);
        Assert.Equal("3", vm.MediaCount);
        Assert.Equal("in this inventory", vm.MediaNote);
        Assert.All(vm.ByMedia, r => Assert.True(r.Selection?.IsChecked));
    }

    [Fact]
    public async Task Unselect_all_shows_no_media_but_keeps_every_media_in_the_table()
    {
        using var vm = await Create();

        vm.UnselectAllMediaCommand.Execute(null);
        await Reloaded(vm, () => vm.MediaCount == "0");

        Assert.True(vm.NoMediaSelected);
        Assert.Equal("No media selected", vm.FilterText);
        Assert.Equal(3, vm.ByMedia.Count);
        Assert.All(vm.ByMedia, r => Assert.Equal("—", r.Duplicates));
        Assert.Empty(vm.ByCategory);
        Assert.Equal("—", vm.FileCount);
    }

    [Fact]
    public async Task Ticking_a_row_in_the_table_updates_the_dashboard()
    {
        using var vm = await Create();
        vm.UnselectAllMediaCommand.Execute(null);
        await Reloaded(vm, () => vm.MediaCount == "0");

        vm.ByMedia.First(r => r.MediaKey == _m2).Selection!.IsChecked = true;
        await Reloaded(vm, () => vm.MediaCount == "1");

        Assert.Equal("M2", vm.FilterText);
        Assert.Equal("selected of 3", vm.MediaNote);
        Assert.True(vm.ByMedia.First(r => r.MediaKey == _m2).Selection!.IsChecked);
    }

    [Fact]
    public async Task Several_quick_changes_reload_once_after_the_delay()
    {
        using var vm = await Create();

        vm.MediaFilter[0].IsChecked = false;
        vm.MediaFilter[1].IsChecked = false;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("3", vm.MediaCount); // not reloaded yet

        _test.Time.Advance(DashboardViewModel.ReloadDelay);
        await WaitUntil(() => vm.MediaCount == "1");
    }

    [Fact]
    public async Task Search_narrows_the_list_and_select_all_applies_to_the_media_shown()
    {
        using var vm = await Create();
        vm.UnselectAllMediaCommand.Execute(null);

        vm.MediaSearch = "m1";
        Assert.Equal(["M1", "M10"], vm.VisibleMediaFilter.Select(m => m.MediaId));

        vm.SelectAllMediaCommand.Execute(null);
        await Reloaded(vm, () => vm.MediaCount == "2");
        Assert.Equal("2 of 3 media", vm.FilterText);
        Assert.False(vm.MediaFilter.Single(m => m.MediaKey == _m2).IsChecked);
    }

    [Fact]
    public async Task Only_selects_a_single_media_and_the_header_toggles_all()
    {
        using var vm = await Create();

        vm.SelectOnlyMediaCommand.Execute(vm.MediaFilter.Single(m => m.MediaKey == _m1));
        Assert.Equal("M1", vm.FilterText);

        vm.ToggleAllMediaCommand.Execute(null);
        Assert.True(vm.AllMediaSelected);
        vm.ToggleAllMediaCommand.Execute(null);
        Assert.True(vm.NoMediaSelected);
        await Reloaded(vm, () => vm.MediaCount == "0");
    }

    [Fact]
    public async Task Click_through_carries_the_whole_selection_to_files()
    {
        using var vm = await Create();
        FileFilter? shown = null;
        _navigator.ShowFilesRequested += (_, f) => shown = f;

        vm.OpenDuplicatesCommand.Execute(null);
        Assert.NotNull(shown);
        Assert.Null(shown.MediaKeys); // all media: no media filter
        Assert.Null(shown.MediaKey);

        vm.MediaFilter.Single(m => m.MediaId == "M10").IsChecked = false;
        vm.OpenDuplicatesCommand.Execute(null);
        Assert.Equal([_m1, _m2], shown!.MediaKeys!.Order());
        Assert.True(shown.DuplicatesOnly);

        vm.SelectOnlyMediaCommand.Execute(vm.MediaFilter.Single(m => m.MediaKey == _m2));
        vm.OpenDuplicatesCommand.Execute(null);
        Assert.Equal(_m2, shown!.MediaKey);
        Assert.Null(shown.MediaKeys);
    }

    private async Task<DashboardViewModel> Create()
    {
        var vm = new DashboardViewModel(_host, _scans, new DashboardQueries(null, NullLogger<DashboardQueries>.Instance), _settings,
            _navigator, NullLogger<DashboardViewModel>.Instance, new InlineUiDispatcher(), _test.Time);
        await WaitUntil(() => vm.ByMedia.Count == 3 && !vm.IsLoading);
        return vm;
    }

    /// <summary>Lets the reload delay pass and waits for the dashboard to show the result.</summary>
    private async Task Reloaded(DashboardViewModel vm, Func<bool> condition)
    {
        _test.Time.Advance(DashboardViewModel.ReloadDelay);
        await WaitUntil(() => condition() && !vm.IsLoading);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The dashboard did not update.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }
}

using Accession.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.WebUi;

public sealed class ComponentTests
{
    private sealed record Row(string Name, long Size);

    // ---- DataTable ----

    [Fact]
    public async Task Data_table_renders_headers_rows_and_sort_state()
    {
        var rows = new[] { new Row("alpha.msg", 10), new Row("beta.pdf", 20) };

        var html = await RenderTable(rows, sortKey: "size", descending: true, selected: rows[1]);

        Assert.Contains(">Name<", html.Replace(" ", string.Empty, StringComparison.Ordinal));
        Assert.Contains("alpha.msg", html);
        Assert.Contains("beta.pdf", html);
        Assert.Contains("aria-sort=\"descending\"", html);
        Assert.Contains("aria-sort=\"none\"", html);
        Assert.Contains("is-selected", html);
    }

    [Fact]
    public async Task Data_table_shows_empty_and_loading_text()
    {
        Assert.Contains("Nothing here", await RenderTable([], emptyText: "Nothing here"));
        Assert.Contains("Loading", await RenderTable([], isLoading: true)); // the ellipsis is HTML-encoded
    }

    [Fact]
    public async Task Virtualized_data_table_does_not_render_every_row_of_a_large_page()
    {
        var rows = Enumerable.Range(0, 50_000).Select(i => new Row($"file{i}.txt", i)).ToList();

        var html = await RenderTable(rows, virtualize: true);

        var renderedRows = html.Split("file", StringSplitOptions.None).Length - 1;
        Assert.True(renderedRows < 500, $"{renderedRows} rows rendered");
    }

    // ---- Pager ----

    [Fact]
    public async Task Pager_shows_range_total_and_page_count()
    {
        var html = await RenderPager(pageIndex: 1, total: 738_639, pageSize: 1_000);

        Assert.Contains("1,001", html);
        Assert.Contains("2,000 of 738,639 files", html);
        Assert.Contains("of 739", html);
        Assert.Contains("value=\"50000\"", html); // largest page size offered
    }

    [Fact]
    public async Task Pager_disables_first_and_previous_on_the_first_page()
    {
        var html = await RenderPager(pageIndex: 0, total: 5_000, pageSize: 1_000);

        Assert.Contains("title=\"First page\" disabled", html);
        Assert.Contains("title=\"Previous page\" disabled", html);
        Assert.DoesNotContain("title=\"Next page\" disabled", html);
    }

    [Fact]
    public async Task Pager_with_no_rows_reads_no_items_and_one_page()
    {
        var html = await RenderPager(pageIndex: 0, total: 0, pageSize: 1_000);

        Assert.Contains("No files", html);
        Assert.Contains("of 1", html);
        Assert.Contains("title=\"Last page\" disabled", html);
    }

    [Theory]
    [InlineData(0, 1000, 1)]
    [InlineData(1000, 1000, 1)]
    [InlineData(1001, 1000, 2)]
    [InlineData(10_000_000, 50_000, 200)]
    public void Pager_page_count(long total, int pageSize, int expected)
    {
        Assert.Equal(expected, Pager.CountPages(total, pageSize));
    }

    [Fact]
    public void File_page_sizes_default_to_1000_and_go_up_to_50000()
    {
        Assert.Equal([1_000, 2_000, 5_000, 10_000, 50_000], Pager.FilePageSizes);
    }

    // ---- Modal, tabs ----

    [Fact]
    public async Task Modal_renders_only_when_open()
    {
        var closed = await WebUiRenderer.RenderAsync<Modal>(new Dictionary<string, object?> { ["IsOpen"] = false, ["Title"] = "Add media" });
        var open = await WebUiRenderer.RenderAsync<Modal>(new Dictionary<string, object?>
        {
            ["IsOpen"] = true,
            ["Title"] = "Add media",
            ["ChildContent"] = (RenderFragment)(b => b.AddContent(0, "Body text")),
        });

        Assert.DoesNotContain("Add media", closed);
        Assert.Contains("role=\"dialog\"", open);
        Assert.Contains("Add media", open);
        Assert.Contains("Body text", open);
    }

    [Fact]
    public async Task Tabs_mark_the_active_tab()
    {
        var html = await WebUiRenderer.RenderAsync<Tabs>(new Dictionary<string, object?>
        {
            ["Items"] = new List<TabItem> { new("details", "Details"), new("history", "Scan history", "3") },
            ["ActiveKey"] = "history",
        });

        Assert.Contains("aria-selected=\"true\"", html);
        Assert.Contains("aria-selected=\"false\"", html);
        Assert.Contains("tab-badge", html);
    }

    // ---- Error boundary ----

    [Fact]
    public async Task Screen_error_boundary_contains_a_failing_screen_and_logs_it()
    {
        var errors = new WebUiRenderer.RecordingErrorLogger();

        var html = await WebUiRenderer.RenderAsync<ScreenErrorBoundary>(new Dictionary<string, object?>
        {
            ["ChildContent"] = (RenderFragment)(_ => throw new InvalidOperationException("boom")),
        }, errors);

        Assert.Contains("This screen ran into a problem", html);
        Assert.Contains("Try again", html);
        Assert.Single(errors.Logged);
    }

    // ---- Toasts ----

    [Fact]
    public void Toasts_close_by_themselves_after_the_duration()
    {
        var time = new FakeTimeProvider();
        var toasts = new ToastService(time);
        var changes = 0;
        toasts.Changed += (_, _) => changes++;

        toasts.Show("Path copied", ToastKind.Success);
        Assert.Single(toasts.Items);

        time.Advance(ToastService.DefaultDuration - TimeSpan.FromMilliseconds(1));
        Assert.Single(toasts.Items);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(toasts.Items);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Toasts_keep_the_five_newest()
    {
        var toasts = new ToastService(new FakeTimeProvider());

        for (var i = 1; i <= 7; i++)
        {
            toasts.Show($"message {i}");
        }

        Assert.Equal(["message 3", "message 4", "message 5", "message 6", "message 7"], toasts.Items.Select(t => t.Message));
    }

    [Fact]
    public void Dismissing_a_toast_removes_it()
    {
        var toasts = new ToastService(new FakeTimeProvider());
        toasts.Show("one");

        toasts.Dismiss(toasts.Items[0].Id);

        Assert.Empty(toasts.Items);
    }

    // ---- helpers ----

    private static Task<string> RenderTable(IReadOnlyList<Row> rows, string? sortKey = null, bool descending = false,
        Row? selected = null, string emptyText = "Nothing to show.", bool isLoading = false, bool virtualize = false)
    {
        RenderFragment columns = b =>
        {
            b.OpenComponent<Column<Row>>(0);
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "SortKey", "name");
            b.AddAttribute(3, "ChildContent", (RenderFragment<Row>)(row => c => c.AddContent(0, row.Name)));
            b.CloseComponent();
            b.OpenComponent<Column<Row>>(4);
            b.AddAttribute(5, "Title", "Size");
            b.AddAttribute(6, "SortKey", "size");
            b.AddAttribute(7, "Class", "num");
            b.AddAttribute(8, "ChildContent", (RenderFragment<Row>)(row => c => c.AddContent(0, row.Size)));
            b.CloseComponent();
        };

        return WebUiRenderer.RenderAsync<DataTable<Row>>(new Dictionary<string, object?>
        {
            ["Columns"] = columns,
            ["Items"] = rows,
            ["SortKey"] = sortKey,
            ["SortDescending"] = descending,
            ["SelectedItem"] = selected,
            ["EmptyText"] = emptyText,
            ["IsLoading"] = isLoading,
            ["Virtualize"] = virtualize,
        });
    }

    private static Task<string> RenderPager(int pageIndex, long total, int pageSize) =>
        WebUiRenderer.RenderAsync<Pager>(new Dictionary<string, object?>
        {
            ["PageIndex"] = pageIndex,
            ["TotalCount"] = total,
            ["PageSize"] = pageSize,
            ["ItemName"] = "files",
        });
}

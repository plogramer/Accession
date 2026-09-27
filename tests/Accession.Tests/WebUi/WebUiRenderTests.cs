using Accession.UI.App;
using Accession.UI.Shell;

namespace Accession.Tests.WebUi;

/// <summary>
/// Renders the web UI to HTML with sample data. Set ACCESSION_UI_PREVIEW_DIR to also write standalone
/// preview pages (with the stylesheet linked) for design review in a browser.
/// </summary>
public sealed class WebUiRenderTests
{
    [Fact]
    public async Task Shell_with_dashboard_renders_all_sections()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard()), "dashboard-light");

        Assert.Contains("Northwind v. Contoso Ltd.", html);
        Assert.Contains("Locked by you", html);
        foreach (var heading in new[] { "By media", "By category", "By extension", "Files by year modified", "Largest files" })
        {
            Assert.Contains(heading, html);
        }

        Assert.Contains("123-123_002", html);
        Assert.Contains("Completed with errors", html);
        Assert.Contains("board_meeting_2020-06.mp4", html);
        Assert.Contains("26,626", html);
        Assert.DoesNotContain("A scan is running", html);
    }

    [Fact]
    public async Task By_media_table_has_a_checkbox_per_media_and_dims_unselected_ones()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard(false, 4, 5)), "dashboard-selection");

        Assert.Contains("Tick media to update the dashboard", html);
        Assert.Contains("aria-label=\"Select all media\"", html);
        Assert.Contains("aria-label=\"Include 123-124_001\"", html);
        Assert.Equal(2, CountOf(html, "is-unselected"));
        Assert.Contains("3 of 5 media", html);
        Assert.Contains("selected of 5", html);
        Assert.Contains("By category", html);
    }

    [Fact]
    public async Task No_media_selected_explains_how_to_select_instead_of_empty_sections()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard(false, 1, 2, 3, 4, 5)), "dashboard-none-selected");

        Assert.Contains("No media selected", html);
        Assert.Contains("Select all media", html);
        Assert.Contains("123-123_001", html); // the table still lists every media
        Assert.DoesNotContain("By category", html);
        Assert.DoesNotContain("Largest files", html);
    }

    [Fact]
    public async Task Dark_theme_sets_the_theme_attribute()
    {
        var html = await RenderAsync(new FakeApp(new FakeShell(new FakeDashboard())) { Theme = "dark" }, "dashboard-dark");

        Assert.Contains("data-theme=\"dark\"", html);
    }

    [Fact]
    public async Task System_theme_leaves_the_theme_to_the_operating_system()
    {
        var html = await RenderAsync(new FakeApp(new FakeShell(new FakeDashboard())) { Theme = "system" });

        Assert.DoesNotContain("data-theme", html);
    }

    [Fact]
    public async Task Running_scan_shows_scan_controls_and_defers_file_table_sections()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard(scanInProgress: true), scanning: true), "dashboard-scanning");

        Assert.Contains("A scan is running", html);
        Assert.Contains("Hashing 41,210 of 96,020 files", html);
        Assert.Contains("title=\"Pause\"", html);
        Assert.Contains("Available when the scan finishes.", html);
        Assert.DoesNotContain("Scan pending", html);
    }

    [Fact]
    public async Task Read_only_inventory_shows_read_only_chip()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard(), readOnly: true));

        Assert.Contains("Read-only", html);
        Assert.DoesNotContain("Locked by you", html);
    }

    [Fact]
    public async Task Screen_without_a_page_says_it_is_not_available()
    {
        var shell = new FakeShell(new FakeDashboard());
        shell.SelectedItem = shell.NavItems.First(n => n.Key == "Categories");

        var html = await RenderAsync(shell);

        Assert.Contains("This screen is not available.", html);
        Assert.DoesNotContain("By media", html);
        Assert.DoesNotContain("classic", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_page_lists_recent_inventories_and_marks_missing_ones()
    {
        var html = await RenderAsync(new FakeApp(new FakeStart()), "start");

        Assert.Contains("New inventory", html);
        Assert.Contains("Open inventory", html);
        Assert.Contains("Fabrikam Arbitration", html);
        Assert.Contains("Not found", html);
        Assert.Contains("Version 0.1.0", html);
        Assert.DoesNotContain("Classic UI", html);
        Assert.DoesNotContain("sidebar", html);
    }

    [Fact]
    public async Task Start_page_without_recent_inventories_explains_the_list()
    {
        var html = await RenderAsync(new FakeApp(new FakeStart(empty: true)));

        Assert.Contains("Inventories you open or create will appear here.", html);
    }

    [Fact]
    public async Task Busy_overlay_shows_the_message()
    {
        var html = await RenderAsync(new FakeApp(new FakeStart()) { IsBusy = true, BusyMessage = "Opening inventory" });

        Assert.Contains("Opening inventory", html);
        Assert.Contains("class=\"spinner\"", html);
    }

    [Fact]
    public async Task Pending_dialog_is_shown_with_its_facts_and_choices()
    {
        var app = new FakeApp(new FakeStart());
        var answer = app.Dialogs.AskAsync(new Accession.UI.Components.ChoiceDialog(
            "Inventory in use", "Another user has this inventory open.",
            [new("cancel", "Cancel"), new("readonly", "Open read-only", Accession.UI.Components.DialogChoiceStyle.Primary)], "cancel")
        {
            Facts = [new("User", "LITSUPPORT\\john.roe"), new("Computer", "LIT-WS-042")],
        });

        var html = await RenderAsync(app, "dialog");

        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("Inventory in use", html);
        Assert.Contains("LIT-WS-042", html);
        Assert.Contains("Open read-only", html);
        Assert.False(answer.IsCompleted);
    }

    private static int CountOf(string html, string text) => (html.Length - html.Replace(text, string.Empty, StringComparison.Ordinal).Length) / text.Length;

    private static Task<string> RenderAsync(IShellModel shell, string? previewName = null) => RenderAsync(new FakeApp(shell), previewName);

    private static async Task<string> RenderAsync(IAppModel app, string? previewName = null)
    {
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = app });
        WritePreview(previewName, html);
        return html;
    }

    internal static void WritePreview(string? name, string body)
    {
        var folder = Environment.GetEnvironmentVariable("ACCESSION_UI_PREVIEW_DIR");
        if (name is null || string.IsNullOrEmpty(folder))
        {
            return;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Accession.sln")))
        {
            dir = dir.Parent;
        }

        var css = new Uri(Path.Combine(dir!.FullName, "src", "Accession.UI", "wwwroot", "css", "accession.css")).AbsoluteUri;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".html"),
            $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" /><link rel=\"stylesheet\" href=\"{css}\" /></head><body><div id=\"app\">{body}</div></body></html>");
    }
}

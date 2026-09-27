using Accession.UI.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task Dark_theme_sets_the_theme_attribute()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard()) { Theme = "dark" }, "dashboard-dark");

        Assert.Contains("data-theme=\"dark\"", html);
    }

    [Fact]
    public async Task System_theme_leaves_the_theme_to_the_operating_system()
    {
        var html = await RenderAsync(new FakeShell(new FakeDashboard()) { Theme = "system" });

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
    public async Task Screen_not_in_web_ui_offers_the_classic_screen_with_the_pending_filter()
    {
        var shell = new FakeShell(new FakeDashboard()) { PendingFilterText = "Media 123-123_001 · Email" };
        shell.SelectedItem = shell.NavItems.First(n => n.Key == "Files");

        var html = await RenderAsync(shell, "classic-only");

        Assert.Contains("Open in classic UI", html);
        Assert.Contains("Media 123-123_001", html); // the separator is HTML-encoded
        Assert.DoesNotContain("By media", html);
    }

    private static async Task<string> RenderAsync(IShellModel shell, string? previewName = null)
    {
        await using var renderer = new HtmlRenderer(new EmptyServices(), NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AppShell>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(AppShell.Model)] = shell }));
            return output.ToHtmlString();
        });

        WritePreview(previewName, html);
        return html;
    }

    private static void WritePreview(string? name, string body)
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

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

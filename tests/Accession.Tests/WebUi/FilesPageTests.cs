using Accession.UI.App;
using Accession.UI.FilesScreen;

namespace Accession.Tests.WebUi;

public sealed class FilesPageTests
{
    [Fact]
    public async Task Files_page_shows_tree_filters_selection_actions_and_pager()
    {
        var html = await RenderInShell(new FakeFiles(), "files");

        Assert.Contains("184,203 files", html);
        Assert.Contains("All media", html);
        Assert.Contains("Engineering", html);
        Assert.Contains("title=\"Link or junction; not followed\"", html);
        Assert.Contains("Filtered by", html);
        Assert.Contains("Copy SHA-1", html);
        Assert.Contains("Show in folder", html);
        Assert.Contains("2,001", html);          // pager: page 3 of 1,000 rows
        Assert.Contains("of 185", html);
        Assert.Contains("aria-sort=\"descending\"", html);
    }

    [Fact]
    public async Task Pager_while_counting_offers_next_but_not_last()
    {
        var html = await RenderInShell(new FakeFiles(rows: 1_000, counting: true));

        Assert.Contains("counting files", html);
        Assert.Contains("title=\"Last page\" disabled", html);
        Assert.DoesNotContain("title=\"Next page\" disabled", html);
    }

    [Fact]
    public async Task No_matching_files()
    {
        var html = await RenderInShell(new FakeFiles(rows: 0));

        Assert.Contains("No files match these filters.", html);
        Assert.Contains("Select a file for actions", html);
    }

    private static async Task<string> RenderInShell(IFilesModel files, string? previewName = null)
    {
        var shell = new FakeShell(new FakeDashboard(), files: files);
        shell.SelectedItem = shell.NavItems.First(n => n.Key == "Files");
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = new FakeApp(shell) });
        if (previewName is not null)
        {
            WebUiRenderTests.WritePreview(previewName, html);
        }

        return html;
    }
}

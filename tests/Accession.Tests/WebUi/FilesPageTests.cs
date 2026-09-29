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

        var header = html[html.IndexOf("<thead>", StringComparison.Ordinal)..html.IndexOf("</thead>", StringComparison.Ordinal)];
        Assert.True(header.IndexOf(">Media<", StringComparison.Ordinal) is > 0 and var media && media < header.IndexOf(">Name<", StringComparison.Ordinal),
            "Media comes before Name");
    }

    [Fact]
    public async Task Saved_searches_tab_lists_them_and_the_table_offers_ticks_and_add()
    {
        var html = await RenderInShell(new FakeFiles(savedSearchShown: true, ticked: 3), "files-saved-searches");

        Assert.Contains("Saved searches", html);
        Assert.Contains("Privileged", html);
        Assert.Contains("1,204 files · 2.31 GB", html);
        Assert.Contains("title=\"Emails with outside counsel&#xA;Created by", html); // description and creator as tooltip
        Assert.Contains(@"Created by LITSUPPORT\jane.doe on 2026-09-21 10:42</span>", html); // shown with the open saved search
        Assert.Contains("New saved search", html);
        Assert.DoesNotContain("Include subfolders", html); // the Media tab is not shown
        Assert.Contains("aria-label=\"Tick every file on this page\"", html);
        Assert.Contains("3 ticked", html);
        Assert.Contains("Add to saved search", html);
        Assert.Contains("Remove 3 ticked", html); // ticked files only: one button, its label says which
        Assert.DoesNotContain("Remove all", html);
        Assert.Contains("title=\"Copy the ticked files, or all results, to another folder\"", html);
    }

    [Fact]
    public async Task Media_tab_by_default_with_removable_chips()
    {
        var html = await RenderInShell(new FakeFiles(), "files-media-tab");

        Assert.Contains("Include subfolders", html);
        Assert.Contains(">Media<", html); // the tree's tab is called Media
        Assert.DoesNotContain("title=\"Media\" value=", html); // no Media drop-down in the filter bar
        Assert.Contains("aria-label=\"Remove In Shares\"", html); // each chip can be removed on its own
        Assert.Contains("<span class=\"tab-badge\">3</span>", html); // saved searches count on the tab
        Assert.DoesNotContain("Remove all", html); // no saved search shown

        Assert.DoesNotContain(" ticked <", html); // no ticked chip
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

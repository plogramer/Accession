using System.Collections.ObjectModel;
using System.Windows.Input;
using Accession.UI.App;
using Accession.UI.FilesScreen;
using Accession.UI.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Tests.WebUi;

public sealed class RecordsPagesTests
{
    [Fact]
    public async Task Categories_page_shows_categories_and_the_selected_categorys_extensions()
    {
        var html = await Render(new FakeCategories(), "Categories", "categories");

        Assert.Contains("Spreadsheets", html);
        Assert.Contains("Browse Email files", html);
        Assert.Contains(".msg", html);
        Assert.Contains("is-empty", html);
        Assert.DoesNotContain(">classic<", html); // every screen is in the web UI now
    }

    [Fact]
    public async Task Audit_page_shows_entries_details_and_pager()
    {
        var html = await Render(new FakeAudit(), "Audit Log", "audit");

        Assert.Contains("Media deleted", html);
        Assert.Contains("LITSUPPORT\\jane.doe", html);
        Assert.Contains("&quot;reason&quot;", html);
        Assert.Contains("of 1,204 entries", html);
    }

    private static async Task<string> Render(object screen, string key, string? preview = null)
    {
        var screens = new Dictionary<string, object>
        {
            [key] = screen,
        };
        var shell = new FakeShell(new FakeDashboard(), media: new FakeMedia(), files: new FakeFiles(), screens: AllScreens(screens));
        shell.SelectedItem = shell.NavItems.First(n => n.Key == key);
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = new FakeApp(shell) });
        if (preview is not null)
        {
            WebUiRenderTests.WritePreview(preview, html);
        }

        return html;
    }

    /// <summary>Gives every nav item a model so none shows the "classic" hint.</summary>
    private static Dictionary<string, object> AllScreens(Dictionary<string, object> screens)
    {
        foreach (var key in new[] { "Scan Queue", "Errors", "Categories", "Audit Log" })
        {
            screens.TryAdd(key, new object());
        }

        return screens;
    }

    private sealed class FakeCategories : ObservableObject, ICategoriesModel
    {
        public FakeCategories()
        {
            Categories.Add(new CategoryRowVm(1, "Email", "Messages and mailboxes (msg, eml, pst, ost, mbox).", "211,430", "620.0 GB", 211_430));
            Categories.Add(new CategoryRowVm(2, "Documents", "Word processing and presentations.", "168,902", "410.0 GB", 168_902));
            Categories.Add(new CategoryRowVm(3, "Spreadsheets", "Excel and other spreadsheets.", "64,118", "190.0 GB", 64_118));
            Categories.Add(new CategoryRowVm(4, "CAD", "Drawings and models.", "0", "0 B", 0));
            SelectedCategory = Categories[0];
            Extensions.Add(new ExtensionCountVm("msg", ".msg", "180,221", "512.4 GB", true));
            Extensions.Add(new ExtensionCountVm("pst", ".pst", "64", "102.7 GB", true));
            Extensions.Add(new ExtensionCountVm("mbox", ".mbox", "0", "0 B", false));
        }

        public ObservableCollection<CategoryRowVm> Categories { get; } = [];
        public CategoryRowVm? SelectedCategory { get; set; }
        public ObservableCollection<ExtensionCountVm> Extensions { get; } = [];
        public ICommand ShowFilesCommand { get; } = new RelayCommand(() => { });
        public ICommand ShowExtensionFilesCommand { get; } = new RelayCommand<object?>(_ => { });
    }

    private sealed class FakeAudit : ObservableObject, IAuditModel
    {
        public FakeAudit()
        {
            PageRows =
            [
                new AuditRowVm(1204, "2026-09-27 15:02", @"LITSUPPORT\jane.doe", "LIT-WS-011", "MediaDeleted", "123-126_001", """{"reason":"Duplicate delivery","files":1204}"""),
                new AuditRowVm(1203, "2026-09-27 14:58", @"LITSUPPORT\jane.doe", "LIT-WS-011", "ScanCompleted", "123-123_003", """{"files":96020}"""),
                new AuditRowVm(1202, "2026-09-27 14:10", @"LITSUPPORT\jane.doe", "LIT-WS-011", "InventoryOpened", "", ""),
                new AuditRowVm(1201, "2026-09-26 18:40", @"LITSUPPORT\john.roe", "LIT-WS-042", "LockForced", "", """{"previousUser":"LITSUPPORT\\jane.doe"}"""),
            ];
            SelectedRow = PageRows[0];
        }

        public IReadOnlyList<SelectOption> ActionFilterOptions { get; } = [new(string.Empty, "All actions"), new("MediaDeleted", "Media deleted")];
        public string ActionFilterValue { get; set; } = string.Empty;
        public IReadOnlyList<SelectOption> UserFilterOptions { get; } = [new(string.Empty, "All users")];
        public string UserFilterValue { get; set; } = string.Empty;
        public string MediaIdText { get; set; } = string.Empty;
        public string FromDateText { get; set; } = string.Empty;
        public string ToDateText { get; set; } = string.Empty;
        public IReadOnlyList<AuditRowVm> PageRows { get; }
        public AuditRowVm? SelectedRow { get; set; }
        public string Details => "{\n  \"reason\": \"Duplicate delivery\",\n  \"files\": 1204\n}";
        public int PageIndex => 0;
        public int AuditPageSize => 500;
        public IReadOnlyList<int> PageSizes { get; } = [500, 1_000];
        public long TotalCount => 1_204;
        public Task GoToPageAsync(int pageIndex) => Task.CompletedTask;
        public Task SetPageSizeAsync(int pageSize) => Task.CompletedTask;
        public ICommand ApplyMediaFilterCommand { get; } = new RelayCommand(() => { });
        public ICommand ClearFiltersCommand { get; } = new RelayCommand(() => { });
        public ICommand RefreshCommand { get; } = new RelayCommand(() => { });
    }
}

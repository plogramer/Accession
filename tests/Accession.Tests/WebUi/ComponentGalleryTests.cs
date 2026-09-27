using Accession.UI.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Accession.Tests.WebUi;

/// <summary>Renders the shared components together, for the design-review preview (ACCESSION_UI_PREVIEW_DIR).</summary>
public sealed class ComponentGalleryTests
{
    private sealed record FileRow(string Name, string Path, string Size, string Modified);

    [Fact]
    public async Task Component_gallery_renders()
    {
        var rows = Enumerable.Range(1, 8)
            .Select(i => new FileRow($"invoice_{i:000}.pdf", $@"\Finance\2021\Q{(i % 4) + 1}", $"{i * 1.7:0.0} MB", $"2021-0{(i % 9) + 1}-14 10:2{i % 10}"))
            .ToList();
        var toasts = new ToastService(TimeProvider.System);
        toasts.Show("Path copied to the clipboard", ToastKind.Success);

        RenderFragment gallery = b =>
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "app");
            b.AddAttribute(2, "data-theme", "light");
            b.OpenElement(3, "div");
            b.AddAttribute(4, "class", "page");
            b.AddAttribute(5, "style", "width:100%");

            b.OpenElement(6, "section");
            b.AddAttribute(7, "class", "card");
            b.OpenComponent<Tabs>(8);
            b.AddAttribute(9, "Items", new List<TabItem> { new("files", "Files", "738,639"), new("errors", "Errors", "39"), new("history", "Scan history") });
            b.AddAttribute(10, "ActiveKey", "files");
            b.CloseComponent();
            b.OpenComponent<DataTable<FileRow>>(11);
            b.AddAttribute(12, "Items", rows);
            b.AddAttribute(13, "SortKey", "size");
            b.AddAttribute(14, "SelectedItem", rows[2]);
            b.AddAttribute(15, "OnRowClick", EventCallback.Factory.Create<FileRow>(this, _ => { }));
            b.AddAttribute(16, "Columns", (RenderFragment)(c =>
            {
                AddColumn(c, 0, "Name", "name", null, r => r.Name);
                AddColumn(c, 10, "Folder", "path", null, r => r.Path);
                AddColumn(c, 20, "Size", "size", "num", r => r.Size);
                AddColumn(c, 30, "Modified", "modified", null, r => r.Modified);
            }));
            b.CloseComponent();
            b.OpenComponent<Pager>(17);
            b.AddAttribute(18, "PageIndex", 1);
            b.AddAttribute(19, "TotalCount", 738_639L);
            b.AddAttribute(20, "PageSize", 1_000);
            b.AddAttribute(21, "ItemName", "files");
            b.CloseComponent();
            b.CloseElement();

            b.CloseElement();

            b.OpenComponent<ToastHost>(22);
            b.AddAttribute(23, "Service", toasts);
            b.CloseComponent();

            b.OpenComponent<Modal>(24);
            b.AddAttribute(25, "IsOpen", true);
            b.AddAttribute(26, "Title", "Delete media");
            b.AddAttribute(27, "ChildContent", (RenderFragment)(m =>
            {
                m.OpenElement(0, "p");
                m.AddContent(1, "This removes 123-123_002 and its 198,455 files from the inventory. The audit log keeps a record.");
                m.CloseElement();
                m.OpenElement(2, "div");
                m.AddAttribute(3, "class", "field");
                m.OpenElement(4, "label");
                m.AddContent(5, "Type the Media ID to confirm");
                m.CloseElement();
                m.OpenElement(6, "input");
                m.AddAttribute(7, "class", "input");
                m.AddAttribute(8, "value", "123-123_00");
                m.CloseElement();
                m.CloseElement();
            }));
            b.AddAttribute(28, "Footer", (RenderFragment)(f =>
            {
                f.OpenElement(0, "button");
                f.AddAttribute(1, "class", "btn");
                f.AddContent(2, "Cancel");
                f.CloseElement();
                f.OpenElement(3, "button");
                f.AddAttribute(4, "class", "btn btn-primary");
                f.AddContent(5, "Delete media");
                f.CloseElement();
            }));
            b.CloseComponent();

            b.CloseElement();
        };

        var html = await WebUiRenderer.RenderAsync<FragmentHost>(new Dictionary<string, object?> { ["Content"] = gallery });
        WebUiRenderTests.WritePreview("components", html);

        Assert.Contains("invoice_003.pdf", html);
        Assert.Contains("Delete media", html);
        Assert.Contains("Path copied", html);
    }

    private static void AddColumn(RenderTreeBuilder b, int seq, string title, string key, string? cssClass, Func<FileRow, string> value)
    {
        b.OpenComponent<Column<FileRow>>(seq);
        b.AddAttribute(seq + 1, "Title", title);
        b.AddAttribute(seq + 2, "SortKey", key);
        b.AddAttribute(seq + 3, "Class", cssClass);
        b.AddAttribute(seq + 4, "ChildContent", (RenderFragment<FileRow>)(row => c => c.AddContent(0, value(row))));
        b.CloseComponent();
    }

    /// <summary>Renders a RenderFragment passed as a parameter.</summary>
    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }
}

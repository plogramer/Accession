using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Presentation.ViewModels;
using Accession.Presentation.WebForms;
using Accession.Tests.TestSupport;
using Accession.UI.App;
using Accession.UI.Forms;

namespace Accession.Tests.WebUi;

/// <summary>Web forms built from the real dialog view models, rendered in the page.</summary>
public sealed class FormDialogTests
{
    [Fact]
    public async Task New_inventory_form_shows_fields_errors_and_browse_buttons()
    {
        var vm = new NewInventoryViewModel(new NoDialogs());
        vm.ClientName = "Northwind Holdings";
        vm.ClientCode = "NW";
        vm.MatterName = "Northwind v. Contoso Ltd.";
        vm.MatterCode = "bad/code";
        vm.RootFolder = @"\\evidence01\intake\NW-2026-0142";

        var html = await Render(DialogForms.Build(vm, () => { })!, "form-new-inventory");

        Assert.Contains("New inventory", html);
        Assert.Contains("Client ID", html);
        Assert.Contains("Browse…", html);
        Assert.Contains("has-error", html);
        Assert.Contains("Create inventory", html);
    }

    [Fact]
    public async Task Settings_form_groups_display_scanning_and_export()
    {
        var html = await Render(DialogForms.Build(new SettingsViewModel(new TestSettings(), new NoDialogs()), () => { })!, "form-settings");

        Assert.Contains("Display", html);
        Assert.Contains("Hashing threads", html);
        Assert.Contains("Restore defaults", html);
        Assert.Contains("type=\"number\"", html);
    }

    [Fact]
    public async Task Delete_form_warns_and_disables_delete_until_confirmed()
    {
        var vm = new DeleteMediaViewModel(new Media { MediaId = "123-123_002", FileCount = 198_455, FolderCount = 2_205, ScanCount = 1, TotalBytes = 912_600_000_000 },
            SizeUnitSystem.Decimal);

        var html = await Render(DialogForms.Build(vm, () => { })!, "form-delete");

        Assert.Contains("Type 123-123_002 to confirm", html);
        Assert.Contains("198,455 file", html);
        Assert.Contains("btn btn-danger\" type=\"button\" disabled", html);
    }

    internal static async Task<string> Render(FormDialog form, string preview)
    {
        var app = new FakeApp(new FakeStart());
        app.Dialogs.Open(form);
        var html = await WebUiRenderer.RenderAsync<AppRoot>(new Dictionary<string, object?> { [nameof(AppRoot.Model)] = app });
        WebUiRenderTests.WritePreview(preview, html);
        return html;
    }
}

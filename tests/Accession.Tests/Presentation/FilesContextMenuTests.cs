using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>Right-click menus on the Files screen: which files they act on, copying values, filtering, and folder actions.</summary>
public sealed class FilesContextMenuTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly RecordingDesktop _desktop = new();

    public FilesContextMenuTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            var m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            var root = ScanRows.AddFolder(scope, m1, @"\M1\");
            var mail = ScanRows.AddFolder(scope, m1, @"\M1\mail\", root);
            for (var i = 0; i < 5; i++)
            {
                ScanRows.AddFile(scope, m1, mail, $"a{i}.msg", 100, sha1: i == 0 ? new string('a', 40) : null);
            }

            ScanRows.AddFile(scope, m1, root, "b.pdf", 100);
            transaction.Commit();
        }

        _host.Open(_test.Session);
    }

    public void Dispose()
    {
        _host.Close();
        _test.Dispose();
    }

    private async Task<WebFilesViewModel> Create()
    {
        var vm = new WebFilesViewModel(_host, new FileBrowserQueries(), new CategoryQueries(), new TestSettings(), _desktop, new NoDialogs(),
            new ToastService(TimeProvider.System), NullLogger<WebFilesViewModel>.Instance);
        await Idle(vm);
        return vm;
    }

    [Fact]
    public async Task A_ticked_row_acts_on_all_ticked_rows_and_any_other_row_on_itself()
    {
        using var vm = await Create();
        var rows = vm.Rows.Where(r => r.Extension == "msg").ToList();
        foreach (var row in rows.Take(3))
        {
            vm.SetChecked(row, true);
        }

        vm.OpenFileMenu(rows[1]);
        Assert.Equal(("3 ticked files", 3), (vm.ContextHeader, vm.ContextTargetCount));
        await vm.CopyTargetNamesCommand.ExecuteAsync(null);
        Assert.Equal(string.Join(Environment.NewLine, rows.Take(3).Select(r => r.Name)), _desktop.Clipboard);

        vm.OpenFileMenu(rows[4]); // not ticked: selected, and alone
        Assert.Equal((rows[4].Name, 1), (vm.ContextHeader, vm.ContextTargetCount));
        Assert.Equal(rows[4].FileId, vm.SelectedRow?.FileId);
        await vm.CopyTargetPathsCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(_test.Root, "M1", "mail", rows[4].Name), _desktop.Clipboard);
    }

    [Fact]
    public async Task Copy_sha1_skips_files_not_hashed_yet()
    {
        using var vm = await Create();
        var rows = vm.Rows.Where(r => r.Extension == "msg").ToList();
        vm.SetChecked(rows[0], true);
        vm.SetChecked(rows[1], true);

        vm.OpenFileMenu(rows[3]);
        await vm.CopyTargetSha1sCommand.ExecuteAsync(null);
        Assert.Null(_desktop.Clipboard); // not hashed: nothing to copy

        vm.OpenFileMenu(rows[0]);
        await vm.CopyTargetSha1sCommand.ExecuteAsync(null);
        Assert.Equal(new string('a', 40), _desktop.Clipboard); // only the hashed one of the two
    }

    [Fact]
    public async Task Filter_by_this_extension()
    {
        using var vm = await Create();
        vm.OpenFileMenu(vm.Rows.Single(r => r.Name == "b.pdf"));

        vm.FilterByExtensionCommand.Execute(null);
        await Idle(vm);

        Assert.Equal("pdf", vm.ExtensionText);
        Assert.Equal(1, vm.TotalCount);
    }

    [Fact]
    public async Task Folder_menu_copies_the_path_and_opens_explorer_only_when_the_folder_exists()
    {
        using var vm = await Create();
        vm.Folders[0].IsExpanded = true;
        var mail = vm.Folders[0].Children.Single();
        vm.OpenFolderMenu(mail);
        Assert.Equal("mail", vm.ContextFolderName);

        vm.CopyFolderPathCommand.Execute(null);
        var path = Path.Combine(_test.Root, "M1", "mail");
        Assert.Equal(path, _desktop.Clipboard);

        vm.OpenFolderInExplorerCommand.Execute(null);
        Assert.Empty(_desktop.Opened); // not there (offline media): a notice instead

        Directory.CreateDirectory(path);
        vm.OpenFolderInExplorerCommand.Execute(null);
        Assert.Equal([path], _desktop.Opened);
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

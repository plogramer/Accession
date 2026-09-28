using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Data.Repositories;
using Accession.Data.SavedSearches;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>Saved searches on the Files screen: the list, showing one, ticking rows, adding and removing files.</summary>
public sealed class FilesSavedSearchTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly ToastService _toasts = new(TimeProvider.System);
    private readonly SavedSearchService _service;
    private readonly ScriptedDialogs _dialogs = new();
    private readonly long _m2;

    public FilesSavedSearchTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            var m1 = media.Insert("M1", @"\M1\", _test.Time.GetUtcNow(), "u");
            _m2 = media.Insert("M2", @"\M2\", _test.Time.GetUtcNow(), "u");
            var f1 = ScanRows.AddFolder(scope, m1, @"\M1\");
            var f2 = ScanRows.AddFolder(scope, _m2, @"\M2\");
            for (var i = 0; i < 120; i++)
            {
                ScanRows.AddFile(scope, m1, f1, $"a{i:000}.msg", 100);
            }

            for (var i = 0; i < 30; i++)
            {
                ScanRows.AddFile(scope, _m2, f2, $"b{i:000}.pdf", 100);
            }

            transaction.Commit();
        }

        _host.Open(_test.Session);
        _service = new SavedSearchService(_test.Factory);
    }

    public void Dispose()
    {
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public async Task Showing_a_saved_search_lists_only_its_files_and_a_folder_leaves_it()
    {
        var id = _service.Create(_test.Session, "PDFs", "from M2");
        _service.AddFiles(_test.Session, id, new FileFilter { MediaKey = _m2 });
        using var vm = await Create();

        var row = Assert.Single(vm.SavedSearches);
        Assert.Equal(("PDFs", "30 files"), (row.Name, row.Files));

        vm.SelectedSavedSearch = row;
        await Idle(vm);
        Assert.Equal(30, vm.TotalCount);
        Assert.Contains(vm.ActiveFilters, c => c.Label == "Saved search: PDFs");

        vm.SelectedFolder = vm.Folders[0];
        await Idle(vm);
        Assert.Null(vm.SelectedSavedSearch);
        Assert.Equal(120, vm.TotalCount);
    }

    [Fact]
    public async Task All_results_go_into_a_new_saved_search_created_on_the_spot()
    {
        using var vm = await Create();
        vm.ExtensionText = "msg";
        vm.ApplyCommand.Execute(null);
        await Idle(vm);
        _dialogs.NextName = "Emails";

        await vm.AddAllToSavedSearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.SavedSearches);
        Assert.Equal(("Emails", 120L), (row.Name, row.FileCount));
        Assert.Contains(_toasts.Items, t => t.Message == "Added 120 files to Emails.");
        Assert.Equal(120, vm.TotalCount); // the results stay in view
    }

    [Fact]
    public async Task Ticked_rows_are_added_removed_and_cleared_when_the_filters_change()
    {
        var id = _service.Create(_test.Session, "Picked", null);
        using var vm = await Create();
        var target = Assert.Single(vm.SavedSearches);

        vm.SetChecked(vm.Rows[0], true);
        vm.SetChecked(vm.Rows[1], true);
        Assert.Equal(2, vm.CheckedFileIds.Count);
        await vm.AddCheckedToSavedSearchCommand.ExecuteAsync(target);
        Assert.Empty(vm.CheckedFileIds);
        Assert.Equal(2, Assert.Single(vm.SavedSearches).FileCount);

        vm.SelectedSavedSearch = vm.SavedSearches[0];
        await Idle(vm);
        vm.SetPageChecked(true);
        Assert.Equal(2, vm.CheckedFileIds.Count);
        _dialogs.Answer = true;
        await vm.RemoveCheckedFromSavedSearchCommand.ExecuteAsync(null);
        await Idle(vm);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, _service.List(_test.Session.Database).Single(s => s.SavedSearchId == id).FileCount);

        vm.SelectedSavedSearch = null;
        await Idle(vm);
        vm.SetChecked(vm.Rows[0], true);
        vm.ExtensionText = "pdf";
        vm.ApplyCommand.Execute(null);
        Assert.Empty(vm.CheckedFileIds);
    }

    [Fact]
    public async Task Edit_and_delete_keep_the_list_in_step()
    {
        _service.Create(_test.Session, "Old name", null);
        using var vm = await Create();
        vm.SelectedSavedSearch = vm.SavedSearches[0];
        await Idle(vm);

        _dialogs.NextName = "New name";
        vm.EditSavedSearchCommand.Execute(vm.SavedSearches[0]);
        Assert.Equal("New name", vm.SavedSearches[0].Name);
        Assert.Equal("New name", vm.SelectedSavedSearch!.Name);
        Assert.Contains(vm.ActiveFilters, c => c.Label == "Saved search: New name");

        _dialogs.Answer = true;
        vm.DeleteSavedSearchCommand.Execute(vm.SavedSearches[0]);
        await Idle(vm);
        Assert.Empty(vm.SavedSearches);
        Assert.Null(vm.SelectedSavedSearch);
        Assert.Equal(150, vm.TotalCount);
    }

    [Fact]
    public async Task New_saved_search_form_asks_for_a_unique_name()
    {
        _service.Create(_test.Session, "Taken", null);
        var vm = new SavedSearchViewModel(_test.Session, _service) { Name = "taken" };
        vm.SaveCommand.Execute(null);

        var html = await Accession.Tests.WebUi.FormDialogTests.Render(Accession.Presentation.WebForms.DialogForms.Build(vm, () => { })!, "form-saved-search");

        Assert.Contains("New saved search", html);
        Assert.Contains("Another saved search already has this name.", html);
        Assert.Contains("Description", html);
        Assert.Contains(">Create<", html);
    }

    private async Task<WebFilesViewModel> Create()
    {
        var workflow = new SavedSearchWorkflow(_host, _service, _dialogs, new BusyTracker(TimeProvider.System), _toasts,
            NullLogger<SavedSearchWorkflow>.Instance);
        var vm = new WebFilesViewModel(_host, new FileBrowserQueries(), new CategoryQueries(), _settings, new RecordingDesktop(), _dialogs,
            _toasts, NullLogger<WebFilesViewModel>.Instance, savedSearches: workflow);
        await Idle(vm);
        return vm;
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

    /// <summary>Answers questions and fills in the saved search dialog.</summary>
    private sealed class ScriptedDialogs : IDialogService
    {
        public string NextName { get; set; } = "Saved";

        public bool Answer { get; set; }

        public bool? ShowDialog(DialogViewModelBase viewModel)
        {
            if (viewModel is not SavedSearchViewModel dialog)
            {
                throw new InvalidOperationException("Unexpected dialog: " + viewModel.Title);
            }

            bool? result = null;
            dialog.CloseRequested += (_, value) => result = value;
            dialog.Name = NextName;
            dialog.SaveCommand.Execute(null);
            return result;
        }

        public bool Confirm(string title, string message) => Answer;

        public void ShowInfo(string title, string message) => throw new InvalidOperationException(title);

        public void ShowWarning(string title, string message) => throw new InvalidOperationException(title);

        public void ShowError(string title, string message, Exception? exception = null) => throw new InvalidOperationException(message, exception);

        public string? PickFolder(string title, string? initialDirectory = null) => null;

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

        public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) => null;
    }
}

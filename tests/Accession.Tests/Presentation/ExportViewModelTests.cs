using Accession.Core.Runtime;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Export;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Presentation.WebForms;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>The Export dialog (section 8.15): defaults, scope, validation, estimate and the request it builds.</summary>
public sealed class ExportViewModelTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly ExportService _export;
    private readonly List<(long Key, string MediaId)> _media = [];

    public ExportViewModelTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            foreach (var id in new[] { "123-123_001", "123-123_002" })
            {
                var key = media.Insert(id, $@"\{id}\", _test.Time.GetUtcNow(), "u");
                _media.Add((key, id));
                var folder = ScanRows.AddFolder(scope, key, $@"\{id}\");
                for (var i = 0; i < 1_500; i++)
                {
                    ScanRows.AddFile(scope, key, folder, $"f{i}.txt", 10);
                }
            }

            transaction.Commit();
        }

        _settings.Update(s => s.DefaultExportFolder = _test.Temp.Path);
        _export = new ExportService(new FileBrowserQueries(), new DashboardQueries(null, NullLogger<DashboardQueries>.Instance),
            _test.Factory, new App(), NullLogger<ExportService>.Instance);
    }

    public void Dispose() => _test.Dispose();

    [Fact]
    public async Task Defaults_follow_the_matter_and_settings()
    {
        _settings.Update(s => s.SplitExportPerMedia = true);
        var vm = Create();

        Assert.Equal(Path.Combine(_test.Temp.Path, "ACME_2026-001_Inventory_20260927.xlsx"), vm.OutputPath);
        Assert.Equal(ExportViewModel.ScopeAll, vm.ScopeValue);
        Assert.True(vm.OneWorkbookPerMedia);
        Assert.True(vm.SummarySheet && vm.MediaSheet && vm.CategoriesSheet && vm.ExtensionsSheet && vm.FilesSheet && vm.ErrorsSheet);
        Assert.DoesNotContain(vm.ScopeOptions, o => o.Value == ExportViewModel.ScopeView);
        await WaitUntil(() => vm.EstimateText.StartsWith("Estimated", StringComparison.Ordinal));
        Assert.Equal("Estimated rows: 3,002 → 2 Files sheets in 2 workbooks.", vm.EstimateText);
    }

    [Fact]
    public async Task Preselected_media_start_with_the_selected_scope()
    {
        var vm = Create(preselected: [_media[1].Key]);

        Assert.Equal(ExportViewModel.ScopeSelected, vm.ScopeValue);
        Assert.True(vm.IsSelectedScope);
        Assert.Equal([false, true], vm.Media.Select(m => m.IsChecked));
        await WaitUntil(() => vm.EstimateText == "Estimated rows: 1,501 → 1 Files sheet.");

        vm.Media[1].IsChecked = false;
        Assert.Equal("No media selected.", vm.EstimateText);
    }

    [Fact]
    public void Export_validates_then_builds_the_request()
    {
        var vm = Create(preselected: [_media[0].Key]);
        bool? closed = null;
        vm.CloseRequested += (_, result) => closed = result;

        vm.SummarySheet = vm.MediaSheet = vm.CategoriesSheet = vm.ExtensionsSheet = vm.FilesSheet = vm.ErrorsSheet = false;
        vm.ExportCommand.Execute(null);
        Assert.Equal("Choose at least one sheet.", vm.ValidationError);
        Assert.Null(closed);

        vm.FilesSheet = true;
        vm.OutputPath = "relative.xlsx";
        Assert.Equal("Enter the full path of the workbook, ending in .xlsx.", vm.ValidationError);

        vm.OutputPath = Path.Combine(_test.Temp.Path, "out.xlsx");
        vm.ExportCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Request);
        Assert.Equal([_media[0].Key], vm.Request.MediaKeys);
        Assert.Equal([ExportSheet.Files], vm.Request.Sheets);
        Assert.Null(vm.Request.FilesView);
    }

    [Fact]
    public void Files_view_scope_passes_the_filter()
    {
        var filter = new FileFilter { Extension = "txt" };
        var vm = Create(filesView: filter, filesViewText: ".txt");

        Assert.Equal(ExportViewModel.ScopeView, vm.ScopeValue);
        Assert.Contains(vm.ScopeOptions, o => o.Value == ExportViewModel.ScopeView);
        vm.ExportCommand.Execute(null);

        Assert.Same(filter, vm.Request!.FilesView);
        Assert.Null(vm.Request.MediaKeys);
    }

    [Fact]
    public void Replacing_an_existing_workbook_asks_first()
    {
        var dialogs = new AnswerDialogs(confirm: false);
        var vm = Create(dialogs: dialogs);
        File.WriteAllText(vm.OutputPath, "old");
        bool? closed = null;
        vm.CloseRequested += (_, result) => closed = result;

        vm.ExportCommand.Execute(null);

        Assert.Equal("Replace files?", dialogs.Asked);
        Assert.Null(closed);
    }

    [Fact]
    public async Task Form_shows_scope_sheets_units_output_and_estimate()
    {
        var vm = Create(preselected: [_media[0].Key]);
        await WaitUntil(() => vm.EstimateText.StartsWith("Estimated", StringComparison.Ordinal));

        var html = await Accession.Tests.WebUi.FormDialogTests.Render(DialogForms.Build(vm, () => { })!, "form-export");

        Assert.Contains("Export to Excel", html);
        Assert.DoesNotContain("Current Files view", html, StringComparison.Ordinal); // only offered from the Files screen
        Assert.Contains("123-123_002", html); // media checklist for the selected scope
        Assert.Contains("Extensions", html);
        Assert.Contains("Size (bytes) and Size (MB)", html);
        Assert.Contains("Estimated rows: 1,501", html);
        Assert.Contains("Browse…", html);
    }

    private ExportViewModel Create(IReadOnlyCollection<long>? preselected = null, FileFilter? filesView = null, string? filesViewText = null,
        IDialogService? dialogs = null) =>
        new(_test.Session, _export, _settings, dialogs ?? new NoDialogs(), new InlineUiDispatcher(), NullLogger.Instance, _media,
            _test.Time.GetUtcNow(), preselected, filesView, filesViewText);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    private sealed class AnswerDialogs(bool confirm) : IDialogService
    {
        public string? Asked { get; private set; }

        public bool Confirm(string title, string message)
        {
            Asked = title;
            return confirm;
        }

        public bool? ShowDialog(DialogViewModelBase viewModel) => throw new NotSupportedException();

        public void ShowInfo(string title, string message) => throw new NotSupportedException();

        public void ShowWarning(string title, string message) => throw new NotSupportedException();

        public void ShowError(string title, string message, Exception? exception = null) => throw new NotSupportedException();

        public string? PickFolder(string title, string? initialDirectory = null) => null;

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

        public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) => null;
    }
}

using System.Globalization;
using Accession.Core.Copying;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Copying;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Presentation.WebForms;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>The Generate copy batch / Copy files dialog (requirements 5.8b): defaults, rules, the request, the form.</summary>
public sealed class CopyViewModelTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly TestSettings _settings = new();
    private readonly CopyService _copy;
    private readonly List<long> _files = [];

    public CopyViewModelTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var key = new MediaRepository(scope).Insert("MED001", @"\MED001\", _test.Time.GetUtcNow(), "u");
            var folder = ScanRows.AddFolder(scope, key, @"\MED001\");
            for (var i = 0; i < 12; i++)
            {
                _files.Add(ScanRows.AddFile(scope, key, folder, $"f{i}.txt", 1_000));
            }

            transaction.Commit();
        }

        _settings.Update(s => s.DefaultExportFolder = _test.Temp.Path);
        _copy = new CopyService(_test.Factory, NullLogger<CopyService>.Instance);
    }

    public void Dispose() => _test.Dispose();

    private CopyViewModel Create(CopyDialogMode mode, IReadOnlyCollection<long>? ticked = null, CopyDialogChoices? last = null) =>
        new(mode, _test.Session, _copy, _settings, new NoDialogs(), new InlineUiDispatcher(), NullLogger.Instance, ticked ?? [],
            FileFilter.None with { Extension = "txt" }, ".txt", _test.Time.GetUtcNow(), last);

    [Fact]
    public async Task Defaults_name_the_batch_after_the_matter_and_the_manifest_follows_it()
    {
        var vm = Create(CopyDialogMode.Batch);

        Assert.Equal(CopyViewModel.ScopeAll, vm.ScopeValue);
        Assert.False(vm.HasTicked);
        var stem = $"ACME_2026-001_CopyBatch_{_test.Time.GetUtcNow().ToLocalTime():yyyyMMdd_HHmm}";
        Assert.Equal(Path.Combine(_test.Temp.Path, stem + ".bat"), vm.BatchPath);
        Assert.Equal(Path.Combine(_test.Temp.Path, stem + "_manifest.csv"), vm.ManifestPath);
        Assert.Equal("copy /Y {source} {destination} >nul", vm.Command);
        Assert.False(vm.IsSequential);
        await WaitUntil(() => vm.EstimateText.StartsWith("12 files", StringComparison.Ordinal));

        vm.BatchPath = Path.Combine(_test.Temp.Path, "prod", "run.bat");
        Assert.Equal(Path.Combine(_test.Temp.Path, "prod", "run_manifest.csv"), vm.ManifestPath);

        vm.ManifestPath = Path.Combine(_test.Temp.Path, "mine.csv"); // once changed, it stays
        vm.BatchPath = Path.Combine(_test.Temp.Path, "other.bat");
        Assert.Equal(Path.Combine(_test.Temp.Path, "mine.csv"), vm.ManifestPath);
    }

    [Fact]
    public void Robocopy_with_sequential_names_is_explained_and_blocked()
    {
        var vm = Create(CopyDialogMode.Batch);
        vm.Destination = _test.Temp.Combine("dest");
        vm.TemplateValue = "robocopy";
        Assert.StartsWith("robocopy ", vm.Command, StringComparison.Ordinal);
        Assert.Empty(vm.TemplateConflict);

        vm.NamingValue = CopyViewModel.NamingSequential;
        Assert.Contains("robocopy cannot rename", vm.TemplateConflict, StringComparison.Ordinal);

        vm.GoCommand.Execute(null);
        Assert.Null(vm.Request);
        Assert.Contains("robocopy cannot rename", vm.ValidationError, StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_the_command_makes_it_custom()
    {
        var vm = Create(CopyDialogMode.Batch);

        vm.Command = "copy -d -g -f {source} {destination}";

        Assert.Equal(CopyViewModel.CustomTemplate, vm.TemplateValue);
        Assert.Equal("copy -d -g -f {source} {destination}", vm.Command);
    }

    [Fact]
    public async Task Ticked_rows_build_the_request_and_the_choices_come_back_next_time()
    {
        var vm = Create(CopyDialogMode.Copy, ticked: [_files[0], _files[1]]);
        await WaitUntil(() => vm.EstimateText.StartsWith("2 files", StringComparison.Ordinal));
        vm.Destination = _test.Temp.Combine("dest");
        vm.NamingValue = CopyViewModel.NamingSequential;
        vm.Prefix = "ABC_";
        vm.Digits = 6;
        vm.Verify = true;
        Assert.Equal("First file: ABC_000001.pdf, then ABC_000002.msg, … in media, folder, name order, all in the destination folder. Files without an extension get no dot.",
            vm.NamingExample);

        vm.GoCommand.Execute(null);

        var request = Assert.IsType<CopyRequest>(vm.Request);
        Assert.Equal([_files[0], _files[1]], request.Filter.FileIds);
        Assert.Equal("2 ticked files", request.ScopeText);
        Assert.Equal(new CopyNaming { Mode = CopyNamingMode.Sequential, Prefix = "ABC_", Digits = 6, StartNumber = 1 }, request.Naming);
        Assert.Equal(new CopyFileOptions(PreserveMetadata: true, Verify: true), vm.Options);

        var again = Create(CopyDialogMode.Copy, last: vm.Choices);
        Assert.Equal(_test.Temp.Combine("dest"), again.Destination);
        Assert.True(again.IsSequential && again.Verify);
        Assert.Equal("ABC_", again.Prefix);
    }

    [Fact]
    public void The_destination_cannot_be_under_the_root()
    {
        var vm = Create(CopyDialogMode.Copy);
        vm.Destination = Path.Combine(_test.Root, "MED001");

        vm.GoCommand.Execute(null);

        Assert.Null(vm.Request);
        Assert.Contains("root", vm.ValidationError, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_lists_copied_skipped_failed_and_cancelled()
    {
        var result = new CopyFilesResult(@"D:\out", @"D:\out.csv", 10, 6, 6, 2, 1, 6_000_000, Cancelled: true, MetadataWarnings: 0);

        var text = CopyWorkflow.Summary(result, SizeUnitSystem.Decimal, CultureInfo.InvariantCulture);

        Assert.Equal("Copied: 6 of 10 files (6.00 MB), 6 verified\nSkipped: 2 (already at the destination)\nFailed: 1. The manifest says why for each file.\n" +
                     "Not copied: 1 (cancelled). The files already copied were kept.\n\nManifest: D:\\out.csv", text);
    }

    [Fact]
    public async Task Forms_render_for_both_modes()
    {
        var batch = Create(CopyDialogMode.Batch, ticked: [_files[0]]);
        await WaitUntil(() => batch.EstimateText.StartsWith("1 file", StringComparison.Ordinal));
        batch.Destination = @"D:\Production\Copy1";
        batch.NamingValue = CopyViewModel.NamingSequential;
        batch.Prefix = "ACME";
        batch.BatchPath = @"D:\Production\ACME_2026-001_CopyBatch.bat";
        batch.ManifestPath = @"D:\Production\ACME_2026-001_CopyBatch_manifest.csv";
        var html = await Accession.Tests.WebUi.FormDialogTests.Render(DialogForms.Build(batch, () => { })!, "form-copy-batch");
        Assert.Contains("Generate copy batch", html);
        Assert.Contains("Ticked rows (1 file)", html);
        Assert.Contains("Command for each file", html);
        Assert.Contains("Write batch file", html);
        Assert.DoesNotContain("Preserve metadata", html);

        var copy = Create(CopyDialogMode.Copy);
        await WaitUntil(() => copy.EstimateText.StartsWith("12 files", StringComparison.Ordinal));
        copy.Destination = @"D:\Production\Copy1";
        copy.ManifestPath = @"D:\Production\ACME_2026-001_Copy_manifest.csv";
        html = await Accession.Tests.WebUi.FormDialogTests.Render(DialogForms.Build(copy, () => { })!, "form-copy-files");
        Assert.Contains("Preserve metadata", html);
        Assert.Contains("Verify each copy (SHA-1)", html);
        Assert.Contains("All results (.txt)", html);
        Assert.DoesNotContain("Command for each file", html);
    }

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
}

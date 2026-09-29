using Accession.Core.Threading;
using Accession.Data.Copying;
using Accession.Data.Repositories;
using Accession.Presentation.Services;
using Accession.Tests.TestSupport;
using Accession.UI.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>Quick Copy (right-click on files): only a folder is asked; flat copies with the original names.</summary>
public sealed class QuickCopyTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly ToastService _toasts = new(TimeProvider.System);
    private readonly List<long> _files = [];

    public QuickCopyTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            var media = new MediaRepository(scope).Insert("MED001", @"\MED001\", _test.Time.GetUtcNow(), "u");
            var root = ScanRows.AddFolder(scope, media, @"\MED001\");
            foreach (var folderPath in new[] { @"\MED001\a\", @"\MED001\b\" })
            {
                var folder = ScanRows.AddFolder(scope, media, folderPath, root);
                var path = Path.Combine(_test.Root, "MED001", folderPath.Split('\\')[2], "abc.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, folderPath);
                _files.Add(ScanRows.AddFile(scope, media, folder, "abc.txt", folderPath.Length));
            }
        }

        _host.Open(_test.Session);
    }

    public void Dispose()
    {
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public async Task Asks_only_for_a_folder_and_copies_flat_with_numbered_names_and_no_manifest()
    {
        var destination = _test.Temp.Combine("picked");
        var workflow = new CopyWorkflow(_host, new CopyService(_test.Factory, NullLogger<CopyService>.Instance), new TestSettings(),
            new NoDialogs(pickedFolder: destination), new BusyTracker(TimeProvider.System), _toasts, new RecordingDesktop(), new InlineUiDispatcher(),
            _test.Time, NullLogger<CopyWorkflow>.Instance);

        await workflow.QuickCopyAsync(_files); // NoDialogs: any dialog but the folder picker fails the test

        Assert.Equal(["abc.txt", "abc_2_.txt"], Directory.GetFiles(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(@"\MED001\a\", File.ReadAllText(Path.Combine(destination, "abc.txt")));
        Assert.Equal(@"\MED001\b\", File.ReadAllText(Path.Combine(destination, "abc_2_.txt")));
        Assert.Empty(Directory.GetFiles(_test.Temp.Path, "*.csv", SearchOption.AllDirectories)); // no manifest anywhere
        Assert.Contains(_toasts.Items, t => t.Message == $"Copied 2 files to {destination}");
    }

    [Fact]
    public void Without_a_manifest_the_summary_lists_the_failed_files()
    {
        var result = new CopyFilesResult(@"D:\Review", null, 14, 2, 0, 0, 12, 100, false, 0)
        {
            Failures = [.. Enumerable.Range(1, CopyFilesResult.MaxFailuresListed).Select(i => new CopyFailure(Path.Combine(_test.Root, "MED001", $"doc{i}.pdf"), "Access is denied."))],
        };

        var text = CopyWorkflow.Summary(result, Accession.Core.Settings.SizeUnitSystem.Binary, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("Failed: 12", text);
        Assert.Contains("  doc1.pdf: Access is denied.", text);
        Assert.Contains("… and 2 more (see the log)", text);
        Assert.DoesNotContain("anifest", text);
    }

    [Fact]
    public async Task Cancelling_the_folder_picker_copies_nothing()
    {
        var workflow = new CopyWorkflow(_host, new CopyService(_test.Factory, NullLogger<CopyService>.Instance), new TestSettings(),
            new NoDialogs(pickedFolder: null), new BusyTracker(TimeProvider.System), _toasts, new RecordingDesktop(), new InlineUiDispatcher(),
            _test.Time, NullLogger<CopyWorkflow>.Instance);

        await workflow.QuickCopyAsync(_files);

        Assert.Empty(_toasts.Items);
    }
}

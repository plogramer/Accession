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
    public async Task Asks_only_for_a_folder_and_copies_flat_with_numbered_names_and_the_manifest_elsewhere()
    {
        var destination = _test.Temp.Combine("picked");
        var manifests = _test.Temp.Combine("manifests");
        var workflow = new CopyWorkflow(_host, new CopyService(_test.Factory, NullLogger<CopyService>.Instance), new TestSettings(),
            new NoDialogs(pickedFolder: destination), new BusyTracker(TimeProvider.System), _toasts, new RecordingDesktop(), new InlineUiDispatcher(),
            _test.Time, NullLogger<CopyWorkflow>.Instance, quickCopyFolder: manifests);

        await workflow.QuickCopyAsync(_files); // NoDialogs: any dialog but the folder picker fails the test

        Assert.Equal(["abc.txt", "abc_2_.txt"], Directory.GetFiles(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(@"\MED001\a\", File.ReadAllText(Path.Combine(destination, "abc.txt")));
        Assert.Equal(@"\MED001\b\", File.ReadAllText(Path.Combine(destination, "abc_2_.txt")));
        Assert.Single(Directory.GetFiles(manifests, "Quick copy *.csv"));
        Assert.Contains(_toasts.Items, t => t.Message == $"Copied 2 files to {destination}");
    }

    [Fact]
    public async Task Cancelling_the_folder_picker_copies_nothing()
    {
        var workflow = new CopyWorkflow(_host, new CopyService(_test.Factory, NullLogger<CopyService>.Instance), new TestSettings(),
            new NoDialogs(pickedFolder: null), new BusyTracker(TimeProvider.System), _toasts, new RecordingDesktop(), new InlineUiDispatcher(),
            _test.Time, NullLogger<CopyWorkflow>.Instance, quickCopyFolder: _test.Temp.Combine("manifests"));

        await workflow.QuickCopyAsync(_files);

        Assert.Empty(_toasts.Items);
        Assert.False(Directory.Exists(_test.Temp.Combine("manifests")));
    }
}

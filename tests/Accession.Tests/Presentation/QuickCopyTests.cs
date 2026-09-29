using Accession.Core.Threading;
using Accession.Data.Copying;
using Accession.Data.Repositories;
using Accession.Presentation.Mvvm;
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
        Assert.Empty(Directory.GetFiles(_test.Temp.Path, "*.csv*", SearchOption.AllDirectories)); // all copied: no manifest
        Assert.Contains(_toasts.Items, t => t.Message == $"Copied 2 files to {destination}");
    }

    [Fact]
    public async Task When_files_fail_it_says_how_many_and_leaves_a_manifest_in_the_folder()
    {
        var destination = _test.Temp.Combine("picked");
        File.Delete(Path.Combine(_test.Root, "MED001", "b", "abc.txt")); // gone since the scan: cannot be copied
        var dialogs = new SummaryDialogs(destination);
        var workflow = new CopyWorkflow(_host, new CopyService(_test.Factory, NullLogger<CopyService>.Instance), new TestSettings(),
            dialogs, new BusyTracker(TimeProvider.System), _toasts, new RecordingDesktop(), new InlineUiDispatcher(),
            _test.Time, NullLogger<CopyWorkflow>.Instance);

        await workflow.QuickCopyAsync(_files);

        var manifest = Assert.Single(Directory.GetFiles(destination, "Quick Copy manifest *.csv"));
        Assert.Equal(2, File.ReadAllLines(manifest).Length - 1); // every file, with its outcome
        Assert.Contains("\"Failed\"", File.ReadAllText(manifest));
        Assert.Empty(Directory.GetFiles(destination, "*.partial"));
        Assert.Equal("Copy finished with failures", dialogs.Title);
        Assert.Contains("Copied: 1 of 2 files", dialogs.Message);
        Assert.Contains("Failed: 1.", dialogs.Message);
        Assert.Contains($"Manifest: {manifest}", dialogs.Message);
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

    /// <summary>Picks the folder and records the summary shown at the end.</summary>
    private sealed class SummaryDialogs(string folder) : IDialogService
    {
        public string Title { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        public string? PickFolder(string title, string? initialDirectory = null) => folder;

        public bool Confirm(string title, string message)
        {
            (Title, Message) = (title, message);
            return false;
        }

        public bool? ShowDialog(DialogViewModelBase viewModel) => throw new InvalidOperationException(viewModel.Title);

        public void ShowInfo(string title, string message) => throw new InvalidOperationException(title);

        public void ShowWarning(string title, string message) => throw new InvalidOperationException(title);

        public void ShowError(string title, string message, Exception? exception = null) => throw new InvalidOperationException(title + ": " + message);

        public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

        public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) => null;
    }
}

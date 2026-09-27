using Accession.Core.Settings;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;

namespace Accession.Tests.TestSupport;

/// <summary>Runs "UI thread" work inline (tests have no dispatcher).</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();

    public void Defer(Action action) => action();

    public T Invoke<T>(Func<T> action) => action();
}

/// <summary>In-memory settings.</summary>
public sealed class TestSettings : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public void Update(Action<AppSettings> change)
    {
        var copy = Current.Clone();
        change(copy);
        copy.Normalize();
        Current = copy;
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(copy));
    }

    public void AddRecentInventory(string path, string displayName)
    {
    }

    public void RemoveRecentInventory(string path)
    {
    }
}

/// <summary>Records desktop calls instead of touching the clipboard or Explorer.</summary>
public sealed class RecordingDesktop : IDesktop
{
    public string? Clipboard { get; private set; }
    public List<string> Opened { get; } = [];

    public void SetClipboardText(string text) => Clipboard = text;

    public void OpenFolder(string path) => Opened.Add(path);

    public void SelectInExplorer(string filePath) => Opened.Add(filePath);

    public void OpenUrl(string url) => Opened.Add(url);

    public void RequestExit()
    {
    }
}

/// <summary>Dialogs that fail the test if shown (and a folder picker returning a set folder).</summary>
public sealed class NoDialogs(string? pickedFolder = null) : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null) => pickedFolder;

    public bool? ShowDialog(DialogViewModelBase viewModel) => throw new InvalidOperationException("Unexpected dialog: " + viewModel.Title);

    public void ShowInfo(string title, string message) => throw new InvalidOperationException("Unexpected message: " + title);

    public void ShowWarning(string title, string message) => throw new InvalidOperationException("Unexpected warning: " + title);

    public void ShowError(string title, string message, Exception? exception = null) => throw new InvalidOperationException("Unexpected error: " + title);

    public bool Confirm(string title, string message) => throw new InvalidOperationException("Unexpected question: " + title);

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => null;

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) => null;
}

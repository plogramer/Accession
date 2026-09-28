namespace Accession.Presentation.Platform;

/// <summary>Windows desktop integration used by view models: clipboard, Explorer, browser, exit.</summary>
public interface IDesktop
{
    void SetClipboardText(string text);

    /// <summary>Opens a folder in Explorer.</summary>
    void OpenFolder(string path);

    /// <summary>Opens Explorer with the file selected. The file itself is never opened (BRW-04).</summary>
    void SelectInExplorer(string filePath);

    /// <summary>Opens a URL in the default browser.</summary>
    void OpenUrl(string url);

    /// <summary>Closes the main window, which runs the normal close flow.</summary>
    void RequestExit();

    /// <summary>Opens the help window at a topic (e.g. "files"), or brings it to the front there.</summary>
    void ShowHelp(string topic);

    /// <summary>Installed WebView2 runtime version (needed by the web UI), or null when it is missing.</summary>
    string? WebViewRuntimeVersion();
}

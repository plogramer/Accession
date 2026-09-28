using System.Diagnostics;
using System.Windows;
using Accession.Presentation.Platform;
using Microsoft.Extensions.Logging;

namespace Accession.App.Platform;

/// <summary><see cref="IDesktop"/> for Windows: WPF clipboard, Explorer and the default browser.</summary>
public sealed class WindowsDesktop(ILogger<WindowsDesktop> logger) : IDesktop
{
    public void SetClipboardText(string text) => Clipboard.SetText(text);

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    public void SelectInExplorer(string filePath) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", filePath }, UseShellExecute = false });

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void RequestExit() => Application.Current.MainWindow?.Close();

    public void ShowHelp(string topic) => Views.HelpWindow.Show(topic);

    public string? WebViewRuntimeVersion()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            logger.LogInformation("WebView2 runtime: {Version}", version ?? "(not found)");
            return version;
        }
        catch (Exception ex)
        {
            // Not installed (WebView2RuntimeNotFoundException) or the loader could not be used (e.g. DllNotFoundException).
            logger.LogError(ex, "WebView2 runtime check failed");
            return null;
        }
    }
}

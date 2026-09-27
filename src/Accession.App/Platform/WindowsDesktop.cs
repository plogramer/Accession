using System.Diagnostics;
using System.Windows;
using Accession.Presentation.Platform;

namespace Accession.App.Platform;

/// <summary><see cref="IDesktop"/> for Windows: WPF clipboard, Explorer and the default browser.</summary>
public sealed class WindowsDesktop : IDesktop
{
    public void SetClipboardText(string text) => Clipboard.SetText(text);

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    public void SelectInExplorer(string filePath) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", filePath }, UseShellExecute = false });

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void RequestExit() => Application.Current.MainWindow?.Close();

    public string? WebViewRuntimeVersion()
    {
        try
        {
            return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            return null;
        }
    }
}

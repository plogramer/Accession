using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Accession.App.Views;

/// <summary>
/// The help window: the bundled help pages (wwwroot/help, offline) in a WebView2. One window is reused; asking for
/// another topic brings it to the front at that topic. Links to the web open in the default browser.
/// </summary>
public sealed class HelpWindow : Window
{
    private static HelpWindow? _open;
    private readonly WebView2 _web = new();

    private HelpWindow()
    {
        Title = "Accession Help";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Accession.ico"));
        Width = 1040;
        Height = 780;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _web;
        _web.CoreWebView2InitializationCompleted += OnWebViewReady;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Closed += (_, _) =>
        {
            _web.Dispose();
            _open = null;
        };
    }

    /// <summary>Shows the help at <paramref name="topic"/> (a section id of the help page).</summary>
    public static void Show(string topic)
    {
        _open ??= new HelpWindow();
        _open.Navigate(topic);
        if (!_open.IsVisible)
        {
            _open.Show();
        }

        if (_open.WindowState == WindowState.Minimized)
        {
            _open.WindowState = WindowState.Normal;
        }

        _open.Activate();
    }

    /// <summary>The help page, file:///…/wwwroot/help/index.html#topic.</summary>
    internal static Uri PageFor(string topic)
    {
        var page = new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "help", "index.html"));
        return new Uri($"{page.AbsoluteUri}#{Uri.EscapeDataString(topic)}");
    }

    private void Navigate(string topic) => _web.Source = PageFor(topic);

    private void OnWebViewReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        // Only the bundled help pages open here; web links go to the default browser.
        core.NavigationStarting += (_, n) =>
        {
            if (!n.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                n.Cancel = true;
                OpenExternally(n.Uri);
            }
        };
        core.NewWindowRequested += (_, n) =>
        {
            n.Handled = true;
            OpenExternally(n.Uri);
        };
    }

    private static void OpenExternally(string uri)
    {
        if (uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
    }
}

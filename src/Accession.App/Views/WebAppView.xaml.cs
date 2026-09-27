using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Accession.Core.Runtime;
using Accession.Presentation.ViewModels;
using Accession.UI.App;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Accession.App.Views;

/// <summary>
/// Hosts the web UI (Accession.UI) in a BlazorWebView. Logs WebView2 problems, and falls back to the classic
/// screens if the page has not rendered within <see cref="StartTimeout"/>, so a failing web view never locks the user out.
/// </summary>
public partial class WebAppView : UserControl
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<WebAppView> _logger = App.Services.GetRequiredService<ILogger<WebAppView>>();
    private BlazorWebView? _webView;
    private DispatcherTimer? _watchdog;

    public WebAppView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => CreateWebView();
        Loaded += (_, _) =>
        {
            if (_webView is null)
            {
                CreateWebView(); // loaded again after an unload
            }
        };
        Unloaded += (_, _) => DisposeWebView();
    }

    private void CreateWebView()
    {
        DisposeWebView();
        if (DataContext is not WebAppViewModel model)
        {
            return;
        }

        var webView = new BlazorWebView
        {
            HostPage = @"wwwroot\index.html",
            Services = App.Services,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        webView.WebView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                _logger.LogError(e.InitializationException, "WebView2 could not start");
            }
        };
        webView.BlazorWebViewInitialized += (_, e) =>
        {
            var core = e.WebView.CoreWebView2;
            _logger.LogInformation("Web UI starting in WebView2 {Version}", core.Environment.BrowserVersionString);

            // The page handles its own shortcuts: no browser reload (F5), find (Ctrl+F), print or zoom reset keys.
            var settings = core.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
#if !DEBUG
            settings.AreDefaultContextMenusEnabled = false; // no "Reload" / "Inspect"; copy and paste keys still work
            settings.AreDevToolsEnabled = false;
#endif
            core.NavigationCompleted += (_, n) =>
            {
                if (!n.IsSuccess)
                {
                    _logger.LogError("Web UI page failed to load: {Status}", n.WebErrorStatus);
                }
            };
            core.ProcessFailed += (_, p) => _logger.LogError("WebView2 process failed: {Kind} ({Reason})", p.ProcessFailedKind, p.Reason);
            core.WebResourceResponseReceived += (_, r) =>
            {
                if (r.Response.StatusCode >= 400)
                {
                    _logger.LogWarning("Web UI resource {Uri} returned {Status}", r.Request.Uri, r.Response.StatusCode);
                }
            };
        };
        webView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(AppRoot),
            Parameters = new Dictionary<string, object?> { [nameof(AppRoot.Model)] = model },
        });

        _webView = webView;
        Host.Children.Add(webView);
        StartWatchdog(model);
    }

    private void StartWatchdog(WebAppViewModel model)
    {
        if (model.IsPageRendered)
        {
            return;
        }

        var watchdog = new DispatcherTimer { Interval = StartTimeout };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            if (!model.IsPageRendered && ReferenceEquals(_watchdog, watchdog))
            {
                _logger.LogError("The web UI did not render within {Seconds} s; switching to the classic screens", StartTimeout.TotalSeconds);
                model.ReportStartFailure(
                    $"The new UI did not start, so Accession switched back to the classic screens.\n\n" +
                    $"Details are in the log: {AppPaths.LogFolder}");
            }
        };
        _watchdog = watchdog;
        watchdog.Start();
    }

    private void DisposeWebView()
    {
        _watchdog?.Stop();
        _watchdog = null;
        if (_webView is not { } webView)
        {
            return;
        }

        _webView = null;
        Host.Children.Clear();

        // Deferred: this can run inside a web view event (e.g. "Close inventory" clicked in the page).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, async () => await webView.DisposeAsync());
    }
}

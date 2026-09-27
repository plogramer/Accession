using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Accession.App.Services;
using Accession.Core.Runtime;
using Accession.Presentation.ViewModels;
using Accession.UI.App;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Accession.App.Views;

/// <summary>
/// Hosts the web UI (Accession.UI) in a BlazorWebView. Logs WebView2 problems, and if the page cannot start
/// (or has not rendered within <see cref="StartTimeout"/>) tells the user and exits instead of showing a blank window.
/// </summary>
public partial class WebAppView : UserControl
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<WebAppView> _logger = App.Services.GetRequiredService<ILogger<WebAppView>>();
    private BlazorWebView? _webView;
    private DispatcherTimer? _watchdog;
    private bool _failed;

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

        try
        {
            CreateWebView(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The web UI could not be created");
            Fail($"The window could not be drawn ({ex.Message}).");
        }
    }

    private void CreateWebView(WebAppViewModel model)
    {
        var webView = new BlazorWebView
        {
            HostPage = @"wwwroot\index.html",
            Services = App.Services,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // The inner WebView2 control only exists once the BlazorWebView is in the window.
        webView.Loaded += (_, _) =>
        {
            if (webView.WebView is { } inner)
            {
                inner.CoreWebView2InitializationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        _logger.LogError(e.InitializationException, "WebView2 could not start");
                    }
                };
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
                _logger.LogError("The web UI did not render within {Seconds} s", StartTimeout.TotalSeconds);
                Fail("The window could not be drawn.");
            }
        };
        _watchdog = watchdog;
        watchdog.Start();
    }

    /// <summary>
    /// The page cannot show anything, including its own dialogs, so a native message explains and the app exits.
    /// Exiting still closes the inventory and stops a scan (App.OnExit).
    /// </summary>
    private void Fail(string reason)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            NativeDialogs.ShowMessage("Accession",
                $"{reason} Accession needs the Microsoft Edge WebView2 Runtime to show its window.\n\n" +
                $"Details are in the log: {AppPaths.LogFolder}",
                MessageBoxImage.Error);
            Application.Current.Shutdown(1);
        });
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Accession.UI.App;
using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace Accession.App.Views;

/// <summary>Hosts the web UI (Accession.UI) in a BlazorWebView.</summary>
public partial class WebAppView : UserControl
{
    private BlazorWebView? _webView;

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
        if (DataContext is not IAppModel model)
        {
            return;
        }

        var webView = new BlazorWebView
        {
            HostPage = @"wwwroot\index.html",
            Services = App.Services,
        };
        webView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(AppRoot),
            Parameters = new Dictionary<string, object?> { [nameof(AppRoot.Model)] = model },
        });
        _webView = webView;
        Host.Content = webView;
    }

    private void DisposeWebView()
    {
        if (_webView is not { } webView)
        {
            return;
        }

        _webView = null;
        Host.Content = null;

        // Deferred: this can run inside a web view event (e.g. "Close inventory" clicked in the page).
        Dispatcher.BeginInvoke(DispatcherPriority.Background, async () => await webView.DisposeAsync());
    }
}

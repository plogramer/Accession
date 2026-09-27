using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Accession.Tests.WebUi;

/// <summary>Renders web UI components to HTML for tests, with the few services components need.</summary>
internal static class WebUiRenderer
{
    public static async Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters, RecordingErrorLogger? errors = null)
        where TComponent : IComponent
    {
        var services = new TestServices(errors ?? new RecordingErrorLogger());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    /// <summary>Stands in for the web view's error boundary logger.</summary>
    internal sealed class RecordingErrorLogger : IErrorBoundaryLogger
    {
        public List<Exception> Logged { get; } = [];

        public ValueTask LogErrorAsync(Exception exception)
        {
            Logged.Add(exception);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestServices(RecordingErrorLogger errors) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IErrorBoundaryLogger) ? errors
            : serviceType == typeof(IJSRuntime) ? NoJavaScript.Instance
            : null;
    }

    /// <summary>Static rendering runs no JavaScript; components that inject IJSRuntime (Virtualize) only need it to exist.</summary>
    private sealed class NoJavaScript : IJSRuntime
    {
        public static readonly NoJavaScript Instance = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}

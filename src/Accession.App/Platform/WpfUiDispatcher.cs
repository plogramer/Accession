using System.Windows;
using System.Windows.Threading;
using Accession.Presentation.Platform;

namespace Accession.App.Platform;

/// <summary><see cref="IUiDispatcher"/> on the WPF dispatcher.</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private static Dispatcher? Dispatcher => Application.Current?.Dispatcher;

    public void Post(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    public void Defer(Action action)
    {
        if (Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public T Invoke<T>(Func<T> action)
    {
        var dispatcher = Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }
}

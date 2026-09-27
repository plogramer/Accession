using System.Windows;
using System.Windows.Threading;
using Accession.Presentation.Platform;

namespace Accession.App.Platform;

/// <summary>Waits in a nested dispatcher loop on the UI thread (as a modal window does), or blocks elsewhere.</summary>
public sealed class WpfModalWaiter : IModalWaiter
{
    public T Wait<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && dispatcher.CheckAccess() && !task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }
}

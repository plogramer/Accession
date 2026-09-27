using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Accession.Core.Threading;

/// <summary>
/// Runs work off the calling thread and exposes a busy state for the UI. The busy indicator only
/// appears when the work takes longer than a short delay (300 ms by default), so quick operations don't flicker.
/// </summary>
public sealed class BusyTracker : INotifyPropertyChanged
{
    public static readonly TimeSpan DefaultShowDelay = TimeSpan.FromMilliseconds(300);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _showDelay;
    private readonly List<Operation> _visible = [];
    private readonly Lock _gate = new();

    public BusyTracker(TimeProvider timeProvider)
        : this(timeProvider, DefaultShowDelay)
    {
    }

    public BusyTracker(TimeProvider timeProvider, TimeSpan showDelay)
    {
        _timeProvider = timeProvider;
        _showDelay = showDelay;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy { get; private set; }

    /// <summary>Message of the most recent visible operation.</summary>
    public string? Message { get; private set; }

    /// <summary>Whether the most recent visible operation can be cancelled.</summary>
    public bool CanCancel { get; private set; }

    public async Task RunAsync(Func<CancellationToken, Task> work, string message, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        await RunAsync<object?>(async token =>
        {
            await work(token).ConfigureAwait(false);
            return null;
        }, message, cancellable).ConfigureAwait(true);
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, string message, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var cancellation = new CancellationTokenSource();
        var operation = new Operation(message, cancellable ? cancellation : null);
        var workTask = Task.Run(() => work(cancellation.Token), CancellationToken.None);

        using (var delayCancellation = new CancellationTokenSource())
        {
            var delayTask = Task.Delay(_showDelay, _timeProvider, delayCancellation.Token);
            var first = await Task.WhenAny(workTask, delayTask).ConfigureAwait(true);
            delayCancellation.Cancel();
            if (first != workTask)
            {
                Show(operation);
            }
        }

        try
        {
            return await workTask.ConfigureAwait(true);
        }
        finally
        {
            Hide(operation);
        }
    }

    /// <summary>Requests cancellation of the most recent visible, cancellable operation.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _visible.LastOrDefault()?.Cancellation?.Cancel();
        }
    }

    private void Show(Operation operation)
    {
        lock (_gate)
        {
            _visible.Add(operation);
        }

        Refresh();
    }

    private void Hide(Operation operation)
    {
        bool removed;
        lock (_gate)
        {
            removed = _visible.Remove(operation);
        }

        if (removed)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        Operation? top;
        int count;
        lock (_gate)
        {
            top = _visible.LastOrDefault();
            count = _visible.Count;
        }

        Set(count > 0, top?.Message, top?.Cancellation is not null);
    }

    private void Set(bool isBusy, string? message, bool canCancel)
    {
        if (Message != message)
        {
            Message = message;
            OnPropertyChanged(nameof(Message));
        }

        if (CanCancel != canCancel)
        {
            CanCancel = canCancel;
            OnPropertyChanged(nameof(CanCancel));
        }

        if (IsBusy != isBusy)
        {
            IsBusy = isBusy;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed record Operation(string Message, CancellationTokenSource? Cancellation);
}

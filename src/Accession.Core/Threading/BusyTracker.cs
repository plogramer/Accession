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

    /// <summary>
    /// Shows the busy indicator now (no delay) until the returned scope is disposed. For work that also runs on the UI
    /// thread (e.g. building the screens of an inventory just opened): the indicator must be up before that thread is
    /// busy, and it keeps animating meanwhile because the page is drawn by the web view's own process.
    /// </summary>
    public BusyScope Begin(string message)
    {
        var operation = new Operation(message, null);
        Show(operation);
        return new BusyScope(this, operation);
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, string message, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        await RunAsync<object?>(async token =>
        {
            await work(token).ConfigureAwait(false);
            return null;
        }, message, cancellable).ConfigureAwait(true);
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, string message, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync((token, _) => work(token), message, cancellable);
    }

    /// <summary>
    /// Like <see cref="RunAsync{T}(Func{CancellationToken, Task{T}}, string, bool)"/>, and the work can update its message
    /// (e.g. progress) through the <see cref="Action{T}"/> it is given. Updates may come from any thread.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Action<string>, Task<T>> work, string message, bool cancellable = false)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var cancellation = new CancellationTokenSource();
        var operation = new Operation(message, cancellable ? cancellation : null);
        var workTask = Task.Run(() => work(cancellation.Token, text => UpdateMessage(operation, text)), CancellationToken.None);

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

    private void UpdateMessage(Operation operation, string message)
    {
        bool visible;
        lock (_gate)
        {
            operation.Message = message;
            visible = _visible.Contains(operation);
        }

        if (visible)
        {
            Refresh();
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

    /// <summary>A busy indicator shown by <see cref="Begin"/>; disposing it hides it.</summary>
    public sealed class BusyScope : IDisposable
    {
        private readonly BusyTracker _owner;
        private readonly Operation _operation;

        internal BusyScope(BusyTracker owner, Operation operation)
        {
            _owner = owner;
            _operation = operation;
        }

        public void Update(string message) => _owner.UpdateMessage(_operation, message);

        public void Dispose() => _owner.Hide(_operation);
    }

    internal sealed class Operation(string message, CancellationTokenSource? cancellation)
    {
        public string Message { get; set; } = message;

        public CancellationTokenSource? Cancellation { get; } = cancellation;
    }
}

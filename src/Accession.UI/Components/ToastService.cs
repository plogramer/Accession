namespace Accession.UI.Components;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>
/// Short, non-blocking notifications ("Path copied"). Shared by the host's view models (which call
/// <see cref="Show"/>) and the <see cref="ToastHost"/> component. Toasts close by themselves.
/// </summary>
public sealed class ToastService(TimeProvider timeProvider)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private readonly object _gate = new();
    private readonly List<Toast> _items = [];
    private readonly Dictionary<Guid, ITimer> _timers = [];

    /// <summary>Raised on any thread when toasts were added or removed.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<Toast> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Show(string message, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind);
        Toast? dropped = null;
        lock (_gate)
        {
            _items.Add(toast);
            if (_items.Count > 5)
            {
                dropped = _items[0];
            }

            // Kept until the toast is dismissed: an unreferenced timer can be collected before it fires.
            _timers[toast.Id] = timeProvider.CreateTimer(_ => Dismiss(toast.Id), null, duration ?? DefaultDuration, Timeout.InfiniteTimeSpan);
        }

        if (dropped is not null)
        {
            Dismiss(dropped.Id);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(t => t.Id == id) > 0;
            if (_timers.Remove(id, out var timer))
            {
                timer.Dispose();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

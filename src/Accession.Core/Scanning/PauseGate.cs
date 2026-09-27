namespace Accession.Core.Scanning;

/// <summary>An async gate workers pass before each item; closed while the scan is paused.</summary>
public sealed class PauseGate
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _open = CreateOpen();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_gate)
        {
            if (!IsPaused)
            {
                IsPaused = true;
                _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            IsPaused = false;
            _open.TrySetResult();
        }
    }

    /// <summary>Completes immediately when not paused; otherwise when resumed or cancelled.</summary>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (_gate)
        {
            task = _open.Task;
        }

        return task.IsCompleted ? Task.CompletedTask : task.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateOpen()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}

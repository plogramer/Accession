namespace Accession.Presentation.Platform;

/// <summary>Runs code on the UI thread. Background callbacks (scan events, settings changes) go through this.</summary>
public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> now when already on the UI thread, otherwise queues it.</summary>
    void Post(Action action);

    /// <summary>Always queues <paramref name="action"/>, even on the UI thread (runs after the current event).</summary>
    void Defer(Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for its result.</summary>
    T Invoke<T>(Func<T> action);
}

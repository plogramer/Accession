namespace Accession.Presentation.Platform;

/// <summary>
/// Waits for a task while keeping the UI responsive (a nested message loop, like a modal window). Lets
/// synchronous dialog calls wait for an answer given in the web page.
/// </summary>
public interface IModalWaiter
{
    T Wait<T>(Task<T> task);
}

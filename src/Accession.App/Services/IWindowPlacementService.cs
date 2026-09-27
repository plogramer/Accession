using System.Windows;

namespace Accession.App.Services;

/// <summary>Remembers window size and position across sessions (stored in user settings).</summary>
public interface IWindowPlacementService
{
    /// <summary>Restores saved placement for <paramref name="key"/>; call before the window is shown.</summary>
    void Restore(Window window, string key);

    void Save(Window window, string key);
}

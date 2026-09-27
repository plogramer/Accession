using Accession.Data.Browsing;

namespace Accession.App.Services;

/// <summary>Lets any screen open the File browser with a filter already applied (requirement DSH-09).</summary>
public sealed class FileBrowserNavigator
{
    /// <summary>Raised when a screen asks to show files; the inventory shell switches to the Files screen.</summary>
    public event EventHandler<FileFilter>? ShowFilesRequested;

    public void ShowFiles(FileFilter filter) => ShowFilesRequested?.Invoke(this, filter);
}

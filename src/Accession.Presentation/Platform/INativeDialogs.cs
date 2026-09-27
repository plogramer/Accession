namespace Accession.Presentation.Platform;

/// <summary>The Windows file and folder pickers, which the web UI keeps instead of drawing its own.</summary>
public interface INativeDialogs
{
    string? PickFolder(string title, string? initialDirectory = null);

    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null);
}

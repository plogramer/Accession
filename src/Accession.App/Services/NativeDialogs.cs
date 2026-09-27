using System.Windows;
using Accession.Presentation.Platform;
using Microsoft.Win32;

namespace Accession.App.Services;

/// <summary>The Windows folder and file pickers, owned by the active window.</summary>
public sealed class NativeDialogs : INativeDialogs
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initialDirectory ?? string.Empty };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FolderName : null;
    }

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = initialDirectory ?? string.Empty,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName ?? string.Empty,
            InitialDirectory = initialDirectory ?? string.Empty,
            OverwritePrompt = true,
            AddExtension = true,
        };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    /// <summary>A message box for when the page cannot show one (start-up, or the web view failed).</summary>
    public static void ShowMessage(string title, string message, MessageBoxImage image)
    {
        var owner = ActiveWindow();
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        }
    }

    private static Window? ActiveWindow()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? (app.MainWindow is { IsVisible: true } main ? main : null);
    }
}

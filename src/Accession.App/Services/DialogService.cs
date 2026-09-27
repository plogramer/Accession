using System.Windows;
using Accession.App.Views;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Accession.App.Services;

public sealed class DialogService(IDesktop desktop, ILogger<DialogService> logger) : IDialogService
{
    public bool? ShowDialog(DialogViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var owner = ActiveWindow();
        var window = new DialogWindow(viewModel)
        {
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        return window.ShowDialog();
    }

    public void ShowInfo(string title, string message) =>
        ShowMessage(title, message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string title, string message)
    {
        logger.LogWarning("Message shown: {Title}: {Message}", title, message);
        ShowMessage(title, message, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string title, string message, Exception? exception = null)
    {
        logger.LogError(exception, "Error shown: {Title}: {Message}", title, message);
        ShowDialog(new ErrorDialogViewModel(title, message, exception, desktop));
    }

    public bool Confirm(string title, string message) =>
        ShowMessage(title, message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

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

    private static MessageBoxResult ShowMessage(string title, string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = ActiveWindow();
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
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

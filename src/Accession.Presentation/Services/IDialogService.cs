using Accession.Presentation.Mvvm;

namespace Accession.Presentation.Services;

/// <summary>Modal dialogs, message boxes and file/folder pickers, behind an interface so view models stay testable.</summary>
public interface IDialogService
{
    /// <summary>Shows <paramref name="viewModel"/> in a modal dialog window and returns the dialog result.</summary>
    bool? ShowDialog(DialogViewModelBase viewModel);

    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    /// <summary>Shows an error with optional technical details (e.g. the exception) the user can copy.</summary>
    void ShowError(string title, string message, Exception? exception = null);

    /// <summary>Yes/No question; returns true for Yes.</summary>
    bool Confirm(string title, string message);

    string? PickFolder(string title, string? initialDirectory = null);

    string? PickOpenFile(string title, string filter, string? initialDirectory = null);

    string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null);
}

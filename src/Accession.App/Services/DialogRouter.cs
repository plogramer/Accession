using Accession.Core.Settings;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;

namespace Accession.App.Services;

/// <summary>Shows dialogs in the web page when the web UI is on, otherwise as WPF windows.</summary>
public sealed class DialogRouter(WebDialogService web, DialogService wpf, ISettingsService settings) : IDialogService
{
    private IDialogService Current => settings.Current.UseWebUi ? web : wpf;

    public bool? ShowDialog(DialogViewModelBase viewModel) => Current.ShowDialog(viewModel);

    public void ShowInfo(string title, string message) => Current.ShowInfo(title, message);

    public void ShowWarning(string title, string message) => Current.ShowWarning(title, message);

    public void ShowError(string title, string message, Exception? exception = null) => Current.ShowError(title, message, exception);

    public bool Confirm(string title, string message) => Current.Confirm(title, message);

    public string? PickFolder(string title, string? initialDirectory = null) => wpf.PickFolder(title, initialDirectory);

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => wpf.PickOpenFile(title, filter, initialDirectory);

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) =>
        wpf.PickSaveFile(title, filter, defaultFileName, initialDirectory);
}

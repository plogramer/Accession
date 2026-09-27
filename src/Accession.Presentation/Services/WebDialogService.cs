using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Accession.Presentation.WebForms;
using Accession.UI.Components;

namespace Accession.Presentation.Services;

/// <summary>
/// Dialogs drawn by the web page. Calls stay synchronous (like modal windows): the caller waits in a nested
/// message loop until the page answers. File and folder pickers remain the native Windows dialogs.
/// </summary>
public sealed class WebDialogService(DialogCenter center, IModalWaiter waiter, INativeDialogs native, IDesktop desktop) : IDialogService
{
    public bool? ShowDialog(DialogViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var result = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var form = DialogForms.Build(viewModel, () => result.TrySetResult(false))
            ?? throw new NotSupportedException($"There is no web form for {viewModel.GetType().Name}.");

        void OnClose(object? sender, bool? value) => result.TrySetResult(value);
        viewModel.CloseRequested += OnClose;
        center.Open(form);
        try
        {
            return waiter.Wait(result.Task);
        }
        finally
        {
            viewModel.CloseRequested -= OnClose;
            center.Close(form);
        }
    }

    public void ShowInfo(string title, string message) => Tell(title, message, DialogKind.Info);

    public void ShowWarning(string title, string message) => Tell(title, message, DialogKind.Warning);

    public void ShowError(string title, string message, Exception? exception = null) =>
        ShowDialog(new ErrorDialogViewModel(title, message, exception, desktop));

    public bool Confirm(string title, string message) =>
        waiter.Wait(center.AskAsync(new ChoiceDialog(title, message,
            [new DialogChoice("no", "No"), new DialogChoice("yes", "Yes", DialogChoiceStyle.Primary)], "no"))) == "yes";

    public string? PickFolder(string title, string? initialDirectory = null) => native.PickFolder(title, initialDirectory);

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null) => native.PickOpenFile(title, filter, initialDirectory);

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null, string? initialDirectory = null) =>
        native.PickSaveFile(title, filter, defaultFileName, initialDirectory);

    private void Tell(string title, string message, DialogKind kind) =>
        waiter.Wait(center.AskAsync(new ChoiceDialog(title, message, [new DialogChoice("ok", "OK", DialogChoiceStyle.Primary)], "ok") { Kind = kind }));
}

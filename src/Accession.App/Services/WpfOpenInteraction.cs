using Accession.Presentation.Services;
using Accession.Presentation.ViewModels;
using Accession.Core.Settings;
using Accession.Data.Locking;
using Accession.Data.Sessions;
using Accession.Presentation.Platform;

namespace Accession.App.Services;

/// <summary>Answers the open workflow's questions with dialogs on the UI thread.</summary>
public sealed class WpfOpenInteraction : IOpenInteraction
{
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;

    public WpfOpenInteraction(IDialogService dialogs, ISettingsService settings, IUiDispatcher ui)
    {
        _ui = ui;
        _dialogs = dialogs;
        _settings = settings;
    }

    public bool ConfirmUpgrade(int fromVersion, int toVersion) => _ui.Invoke(() => _dialogs.Confirm(
        "Upgrade inventory",
        $"This inventory was created by an older version of Accession (schema version {fromVersion}). " +
        $"It must be upgraded to version {toVersion} before it can be opened.\n\n" +
        "A backup copy of the inventory file is made first. Upgrade now?"));

    public LockConflictChoice ResolveLockConflict(LockConflict conflict) => _ui.Invoke(() =>
    {
        if (conflict.IsOtherWindowHere)
        {
            return _dialogs.Confirm("Inventory already open",
                "This inventory is already open in another Accession window on this computer.\n\nOpen it read-only here?")
                ? LockConflictChoice.OpenReadOnly
                : LockConflictChoice.Cancel;
        }

        var viewModel = new LockConflictViewModel(conflict.Holder, conflict.IsStale, _dialogs, _settings.Current.DisplayTimeZone);
        _dialogs.ShowDialog(viewModel);
        return viewModel.Choice;
    });

    public RootUnreachableResolution ResolveRootUnreachable(string rootPath) => _ui.Invoke(() =>
    {
        var viewModel = new RootUnreachableViewModel(rootPath, _dialogs);
        _dialogs.ShowDialog(viewModel);
        return viewModel.Resolution;
    });
}

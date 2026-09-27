using Accession.App.ViewModels;
using Accession.Core.Settings;
using Accession.Data.Locking;
using Accession.Data.Sessions;

namespace Accession.App.Services;

/// <summary>Answers the open workflow's questions with dialogs on the UI thread.</summary>
public sealed class WpfOpenInteraction : IOpenInteraction
{
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    public WpfOpenInteraction(IDialogService dialogs, ISettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
    }

    public bool ConfirmUpgrade(int fromVersion, int toVersion) => UiThread.Invoke(() => _dialogs.Confirm(
        "Upgrade inventory",
        $"This inventory was created by an older version of Accession (schema version {fromVersion}). " +
        $"It must be upgraded to version {toVersion} before it can be opened.\n\n" +
        "A backup copy of the inventory file is made first. Upgrade now?"));

    public LockConflictChoice ResolveLockConflict(LockHolder holder, bool isStale) => UiThread.Invoke(() =>
    {
        var viewModel = new LockConflictViewModel(holder, isStale, _dialogs, _settings.Current.DisplayTimeZone);
        _dialogs.ShowDialog(viewModel);
        return viewModel.Choice;
    });

    public RootUnreachableResolution ResolveRootUnreachable(string rootPath) => UiThread.Invoke(() =>
    {
        var viewModel = new RootUnreachableViewModel(rootPath, _dialogs);
        _dialogs.ShowDialog(viewModel);
        return viewModel.Resolution;
    });
}

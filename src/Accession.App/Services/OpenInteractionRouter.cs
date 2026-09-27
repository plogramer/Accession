using Accession.Core.Settings;
using Accession.Data.Sessions;
using Accession.Presentation.Services;

namespace Accession.App.Services;

/// <summary>Sends the open workflow's questions to web dialogs when the web UI is on, otherwise to WPF dialogs.</summary>
public sealed class OpenInteractionRouter(WebOpenInteraction web, WpfOpenInteraction wpf, ISettingsService settings) : IOpenInteraction
{
    private IOpenInteraction Current => settings.Current.UseWebUi ? web : wpf;

    public bool ConfirmUpgrade(int fromVersion, int toVersion) => Current.ConfirmUpgrade(fromVersion, toVersion);

    public LockConflictChoice ResolveLockConflict(LockConflict conflict) => Current.ResolveLockConflict(conflict);

    public RootUnreachableResolution ResolveRootUnreachable(string rootPath) => Current.ResolveRootUnreachable(rootPath);
}

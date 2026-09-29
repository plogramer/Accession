using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Components;

namespace Accession.UI.App;

/// <summary>
/// The whole web page: the current screen (<c>IStartModel</c> or <c>IShellModel</c>), theme, busy overlay,
/// notifications and dialogs. Implemented by the host application.
/// </summary>
public interface IAppModel : INotifyPropertyChanged
{
    /// <summary>The Start screen's model, or the inventory shell's model while an inventory is open.</summary>
    object Screen { get; }

    /// <summary>"system", "light" or "dark".</summary>
    string Theme { get; set; }

    ToastService Toasts { get; }

    DialogCenter Dialogs { get; }

    // Busy overlay drawn by the page, below its dialogs (the window's own overlay would cover them)
    bool IsBusy { get; }
    string BusyMessage { get; }
    bool CanCancelBusy { get; }
    ICommand CancelBusyCommand { get; }

    /// <summary>Called by the page after its first render (the host's start-up check).</summary>
    void PageRendered();

    // Keyboard shortcuts (Ctrl+N, Ctrl+O, Ctrl+,)
    ICommand NewInventoryCommand { get; }
    ICommand OpenInventoryCommand { get; }
    ICommand OpenSettingsCommand { get; }

    /// <summary>Opens the help window; parameter: the topic (see <see cref="HelpTopics"/>), or null for the contents.</summary>
    ICommand HelpCommand { get; }

    // New versions (GitHub releases)

    /// <summary>"Accession 0.2 is available. You have 0.1." while a newer version is available; empty otherwise.</summary>
    string UpdateText { get; }

    /// <summary>The release notes of the newer version.</summary>
    ICommand ShowUpdateCommand { get; }

    /// <summary>Opens the newer version's page on GitHub.</summary>
    ICommand DownloadUpdateCommand { get; }

    /// <summary>Do not announce this version again.</summary>
    ICommand SkipUpdateCommand { get; }

    /// <summary>Hides the banner until the next start.</summary>
    ICommand DismissUpdateCommand { get; }

    /// <summary>Asks GitHub now and shows the outcome.</summary>
    ICommand CheckForUpdatesCommand { get; }
}

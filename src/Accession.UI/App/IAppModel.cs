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

    // Busy overlay (WPF overlays cannot draw over the web view, so the page draws its own)
    bool IsBusy { get; }
    string BusyMessage { get; }
    bool CanCancelBusy { get; }
    ICommand CancelBusyCommand { get; }

    /// <summary>Switches back to the classic (WPF) screens.</summary>
    ICommand SwitchToClassicCommand { get; }

    // Keyboard shortcuts (Ctrl+N, Ctrl+O, Ctrl+,)
    ICommand NewInventoryCommand { get; }
    ICommand OpenInventoryCommand { get; }
    ICommand OpenSettingsCommand { get; }
}

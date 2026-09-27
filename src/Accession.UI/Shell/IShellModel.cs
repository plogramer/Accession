using System.ComponentModel;
using System.Windows.Input;
using Accession.UI.Components;

namespace Accession.UI.Shell;

/// <summary>
/// The web shell shown while an inventory is open (requirements 8.4): navigation, top bar, status bar.
/// Implemented by the host application; commands that open Windows dialogs stay in the host.
/// </summary>
public interface IShellModel : INotifyPropertyChanged
{
    // Inventory
    string ClientName { get; }
    string MatterName { get; }
    string MatterCode { get; }
    string UserName { get; }
    string RootPath { get; }
    string SchemaText { get; }
    string LockStatus { get; }
    bool IsReadOnly { get; }
    bool IsOffline { get; }
    string Notice { get; }

    // Scan
    string ScanStatus { get; }
    bool IsScanActive { get; }

    // Busy overlay (WPF overlays cannot draw over the web view, so the page draws its own)
    bool IsBusy { get; }
    string BusyMessage { get; }
    bool CanCancelBusy { get; }

    // Navigation
    IReadOnlyList<ShellNavItem> NavItems { get; }
    ShellNavItem SelectedItem { get; set; }

    /// <summary>Set when a Dashboard click-through targets a screen that is not in the web UI yet.</summary>
    string PendingFilterText { get; }

    /// <summary>Short notifications shown in the corner of the page.</summary>
    ToastService Toasts { get; }

    /// <summary>"system", "light" or "dark".</summary>
    string Theme { get; set; }

    // Commands
    ICommand ScanNowCommand { get; }
    ICommand PauseScanCommand { get; }
    ICommand ResumeScanCommand { get; }
    ICommand CancelScanCommand { get; }
    ICommand AddMediaCommand { get; }
    ICommand DiscoverMediaCommand { get; }
    ICommand ShowPropertiesCommand { get; }
    ICommand ChangeRootPathCommand { get; }
    ICommand OpenMatterLinkCommand { get; }
    ICommand OpenSettingsCommand { get; }
    ICommand CloseInventoryCommand { get; }
    ICommand DismissNoticeCommand { get; }
    ICommand CancelBusyCommand { get; }

    /// <summary>Switches to the classic (WPF) screens, opening the selected screen there.</summary>
    ICommand OpenInClassicCommand { get; }
}

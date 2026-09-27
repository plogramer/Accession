using System.ComponentModel;
using System.Windows.Input;

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

    /// <summary>The matter's web link (http/https), or empty when there is none.</summary>
    string MatterUrl { get; }

    string UserName { get; }
    string RootPath { get; }
    string SchemaText { get; }
    bool IsReadOnly { get; }

    /// <summary>Read-only chip text, e.g. "Read-only · jane.doe on LIT-PC12" (only shown while read-only).</summary>
    string ReadOnlyText { get; }

    /// <summary>Why the inventory is read-only and how to edit it.</summary>
    string ReadOnlyTooltip { get; }

    bool IsOffline { get; }
    string Notice { get; }

    // Scan
    string ScanStatus { get; }
    bool IsScanActive { get; }

    // Navigation
    IReadOnlyList<ShellNavItem> NavItems { get; }
    ShellNavItem SelectedItem { get; set; }

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

    /// <summary>Export to Excel… (the Export dialog).</summary>
    ICommand ExportCommand { get; }
    ICommand DismissNoticeCommand { get; }
}

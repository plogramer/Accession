using System.ComponentModel;
using Accession.App.Mvvm;
using Accession.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Accession.App.ViewModels.Shell;

/// <summary>Main window content while an inventory is open (requirements 8.4): navigation, screen, status bar.</summary>
public sealed partial class InventoryShellViewModel : ViewModelBase
{
    private readonly InventoryHost _host;

    public InventoryShellViewModel(InventoryHost host)
    {
        _host = host;
        NavItems =
        [
            new NavItem("Dashboard", new PlaceholderViewModel("Dashboard", "Totals by media, category and extension.", "Dashboard epic (#7)")),
            new NavItem("Media", new PlaceholderViewModel("Media", "Registered media, their status and scan history.", "Media management epic (#4)")),
            new NavItem("Files", new PlaceholderViewModel("Files", "Browse and filter every inventoried file.", "File browser epic (#8)")),
            new NavItem("Scan Queue", new PlaceholderViewModel("Scan Queue", "Scan progress, pause, resume and cancel.", "Scan UI epic (#6)")),
            new NavItem("Errors", new PlaceholderViewModel("Errors", "Access denied, locked files and other scan errors.", "Scan UI epic (#6)")),
            new NavItem("Categories", new PlaceholderViewModel("Categories", "File categories and their extensions.", "File browser epic (#8)")),
            new NavItem("Audit Log", new PlaceholderViewModel("Audit Log", "Who did what and when.", "File browser epic (#8)")),
        ];
        SelectedItem = NavItems[0];
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem SelectedItem { get; set; }

    public string RootPath => _host.Config?.RootPath ?? string.Empty;

    public string LockStatus => _host.Session switch
    {
        null => string.Empty,
        { IsReadOnly: true } => "READ-ONLY",
        _ => "Locked by you",
    };

    public string SchemaText => _host.Config is { } config ? $"Schema v{config.SchemaVersion}" : string.Empty;

    public bool IsOffline => _host.HasSession && !_host.IsRootAvailable;

    public override void OnNavigatedTo() => _host.PropertyChanged += OnHostChanged;

    public override void OnNavigatedFrom() => _host.PropertyChanged -= OnHostChanged;

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace Accession.UI.Start;

/// <summary>The Start screen (requirements 8.2): new/open inventory and the recent list.</summary>
public interface IStartModel : INotifyPropertyChanged
{
    string AppName { get; }
    string Tagline { get; }
    string Version { get; }
    ObservableCollection<RecentInventoryItem> RecentInventories { get; }

    ICommand NewInventoryCommand { get; }
    ICommand OpenInventoryCommand { get; }

    /// <summary>Parameter: the <see cref="RecentInventoryItem"/>.</summary>
    ICommand OpenRecentCommand { get; }

    /// <summary>Parameter: the <see cref="RecentInventoryItem"/>.</summary>
    ICommand RemoveRecentCommand { get; }

    ICommand OpenSettingsCommand { get; }
}

/// <summary>An inventory in the recent list.</summary>
public sealed record RecentInventoryItem(string DisplayName, string Path, string LastOpened, bool Exists);

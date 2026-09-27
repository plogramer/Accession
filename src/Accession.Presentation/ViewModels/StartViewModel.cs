using System.Collections.ObjectModel;
using System.IO;
using Accession.Core.Formatting;
using Accession.Core.Runtime;
using Accession.Core.Settings;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>Start window content (requirements 8.2): new/open inventory and the recent list.</summary>
public sealed partial class StartViewModel : ViewModelBase
{
    private readonly IUiDispatcher _ui;
    private readonly InventoryWorkflows _workflows;
    private readonly ISettingsService _settings;

    public StartViewModel(InventoryWorkflows workflows, ISettingsService settings, IAppInfo appInfo, IUiDispatcher ui)
    {
        _ui = ui;
        _workflows = workflows;
        _settings = settings;
        Version = appInfo.Version;
    }

    public string AppName => "Accession";

    public string Tagline => "Inventory, hash, and report every media you receive.";

    public string Version { get; }

    public ObservableCollection<RecentInventoryItem> RecentInventories { get; } = [];

    public bool HasRecentInventories => RecentInventories.Count > 0;

    public override void OnNavigatedTo()
    {
        _settings.SettingsChanged += OnSettingsChanged;
        LoadRecent(_settings.Current);
    }

    public override void OnNavigatedFrom() => _settings.SettingsChanged -= OnSettingsChanged;

    [RelayCommand]
    private Task NewInventory() => _workflows.NewInventoryAsync();

    [RelayCommand]
    private Task OpenInventory() => _workflows.OpenInventoryAsync();

    [RelayCommand]
    private Task OpenRecent(RecentInventoryItem? item) =>
        item is null ? Task.CompletedTask : _workflows.OpenInventoryAsync(item.Path);

    [RelayCommand]
    private void RemoveRecent(RecentInventoryItem? item)
    {
        if (item is not null)
        {
            _settings.RemoveRecentInventory(item.Path);
        }
    }

    [RelayCommand]
    private void OpenSettings() => _workflows.OpenSettings();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => _ui.Post(() => LoadRecent(e.Settings));

    private void LoadRecent(AppSettings settings)
    {
        RecentInventories.Clear();
        foreach (var recent in settings.RecentInventories)
        {
            RecentInventories.Add(new RecentInventoryItem(
                recent.DisplayName,
                recent.Path,
                TimeFormatter.Format(recent.LastOpenedUtc, settings.DisplayTimeZone),
                File.Exists(recent.Path)));
        }

        OnPropertyChanged(nameof(HasRecentInventories));
    }
}

public sealed record RecentInventoryItem(string DisplayName, string Path, string LastOpened, bool Exists);

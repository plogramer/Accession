using System.Reflection;
using Accession.App.Mvvm;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>Placeholder landing screen until the Start window (inventory list) is built.</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private const long SampleBytes = 5_000_000_000;

    private readonly ISettingsService _settings;
    private readonly MainWindowViewModel _mainWindow;

    public HomeViewModel(ISettingsService settings, MainWindowViewModel mainWindow)
    {
        _settings = settings;
        _mainWindow = mainWindow;
        SampleSize = FormatSample(settings.Current.SizeUnit);
    }

    public string AppName => "Accession";

    public string Tagline => "Inventory, hash, and report every media you receive.";

    public string Version { get; } =
        typeof(HomeViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? string.Empty;

    /// <summary>Shows the current size unit setting, e.g. "5,000,000,000 bytes = 5.00 GB".</summary>
    [ObservableProperty]
    public partial string SampleSize { get; set; }

    public IRelayCommand OpenSettingsCommand => _mainWindow.OpenSettingsCommand;

    public override void OnNavigatedTo() => _settings.SettingsChanged += OnSettingsChanged;

    public override void OnNavigatedFrom() => _settings.SettingsChanged -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => SampleSize = FormatSample(e.Settings.SizeUnit);

    private static string FormatSample(SizeUnitSystem unit) =>
        $"{SampleBytes:N0} bytes = {SizeFormatter.Format(SampleBytes, unit)}";
}

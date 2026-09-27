using System.ComponentModel.DataAnnotations;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>Settings dialog (requirements 5.11 / 8.17). Edits a copy; nothing is saved until OK.</summary>
public sealed partial class SettingsViewModel : DialogViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        Title = "Settings";
        Load(settings.Current);
    }

    public IReadOnlyList<Option<SizeUnitSystem>> SizeUnitOptions { get; } =
    [
        new(SizeUnitSystem.Decimal, "Decimal – KB, MB, GB, TB (1 GB = 1,000,000,000 bytes)"),
        new(SizeUnitSystem.Binary, "Binary – KiB, MiB, GiB, TiB (1 GiB = 1,073,741,824 bytes)"),
    ];

    public IReadOnlyList<Option<DisplayTimeZone>> TimeZoneOptions { get; } =
    [
        new(DisplayTimeZone.Local, "Local time"),
        new(DisplayTimeZone.Utc, "UTC"),
    ];

    public string EnumerationThreadsRange => $"{SettingsLimits.MinEnumerationThreads}–{SettingsLimits.MaxEnumerationThreads}";

    public string HashingThreadsRange => $"{SettingsLimits.MinHashingThreads}–{SettingsLimits.MaxHashingThreads}";

    public string DbBatchSizeRange => $"{SettingsLimits.MinDbBatchSize:N0}–{SettingsLimits.MaxDbBatchSize:N0}";

    public string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial SizeUnitSystem SizeUnit { get; set; }

    [ObservableProperty]
    public partial DisplayTimeZone DisplayTimeZone { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    [Range(SettingsLimits.MinEnumerationThreads, SettingsLimits.MaxEnumerationThreads,
        ErrorMessage = "Enter a number from 1 to 16.")]
    public partial int EnumerationThreads { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    [Range(SettingsLimits.MinHashingThreads, SettingsLimits.MaxHashingThreads,
        ErrorMessage = "Enter a number from 1 to 32.")]
    public partial int HashingThreads { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    [Range(SettingsLimits.MinDbBatchSize, SettingsLimits.MaxDbBatchSize,
        ErrorMessage = "Enter a number from 1,000 to 100,000.")]
    public partial int DbBatchSize { get; set; }

    /// <summary>Empty means the Documents folder.</summary>
    [ObservableProperty]
    public partial string DefaultExportFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SplitExportPerMedia { get; set; }

    [RelayCommand]
    private void BrowseExportFolder()
    {
        var initial = string.IsNullOrWhiteSpace(DefaultExportFolder) ? DocumentsFolder : DefaultExportFolder;
        var folder = _dialogs.PickFolder("Default export folder", initial);
        if (folder is not null)
        {
            DefaultExportFolder = folder;
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        var defaults = new AppSettings();
        Load(defaults);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Ok()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            return;
        }

        _settings.Update(s =>
        {
            s.SizeUnit = SizeUnit;
            s.DisplayTimeZone = DisplayTimeZone;
            s.EnumerationThreads = EnumerationThreads;
            s.HashingThreads = HashingThreads;
            s.DbBatchSize = DbBatchSize;
            s.DefaultExportFolder = DefaultExportFolder;
            s.SplitExportPerMedia = SplitExportPerMedia;
        });
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool CanSave() => !HasErrors;

    private void Load(AppSettings settings)
    {
        SizeUnit = settings.SizeUnit;
        DisplayTimeZone = settings.DisplayTimeZone;
        EnumerationThreads = settings.EnumerationThreads;
        HashingThreads = settings.HashingThreads;
        DbBatchSize = settings.DbBatchSize;
        DefaultExportFolder = settings.DefaultExportFolder;
        SplitExportPerMedia = settings.SplitExportPerMedia;
    }
}

/// <summary>A value with a display label, for combo boxes.</summary>
public sealed record Option<T>(T Value, string Label);

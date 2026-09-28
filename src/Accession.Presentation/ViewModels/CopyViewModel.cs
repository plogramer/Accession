using System.Globalization;
using System.ComponentModel;
using Accession.Core.Copying;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Copying;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.ViewModels;

public enum CopyDialogMode
{
    /// <summary>Write a .bat file with one copy command per file (CPY-05).</summary>
    Batch,

    /// <summary>Copy the files in the app (CPY-06).</summary>
    Copy,
}

/// <summary>The dialog's last choices, offered again next time (for this run of the app).</summary>
public sealed record CopyDialogChoices(string Destination, CopyNaming Naming, string TemplateName, string Command, bool PreserveMetadata, bool Verify);

/// <summary>"Generate copy batch" and "Copy files" dialog (requirements 5.8b).</summary>
public sealed partial class CopyViewModel : DialogViewModelBase
{
    public const string ScopeTicked = "ticked";
    public const string ScopeAll = "all";
    public const string NamingPreserve = "preserve";
    public const string NamingSequential = "sequential";
    public const string CustomTemplate = "custom";

    private readonly InventorySession _session;
    private readonly CopyService _copy;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly ILogger _logger;
    private readonly IReadOnlyCollection<long> _ticked;
    private readonly FileFilter _allResults;
    private readonly SizeUnitSystem _unit;
    private CancellationTokenSource? _estimateCancel;
    private long? _fileCount;
    private bool _tried;
    private bool _manifestEdited;
    private bool _settingManifest;
    private bool _settingCommand;

    /// <param name="ticked">FileIds of the rows ticked in the table.</param>
    /// <param name="allResults">The Files screen's current filter.</param>
    /// <param name="allResultsText">What that filter shows, e.g. ".msg · MED001".</param>
    public CopyViewModel(CopyDialogMode mode, InventorySession session, CopyService copy, ISettingsService settings, IDialogService dialogs,
        IUiDispatcher ui, ILogger logger, IReadOnlyCollection<long> ticked, FileFilter allResults, string allResultsText, DateTimeOffset now,
        CopyDialogChoices? last = null)
    {
        Mode = mode;
        _session = session;
        _copy = copy;
        _dialogs = dialogs;
        _ui = ui;
        _logger = logger;
        _ticked = ticked;
        _allResults = allResults;
        _unit = settings.Current.SizeUnit;
        AllResultsText = allResultsText;
        Title = mode == CopyDialogMode.Batch ? "Generate copy batch" : "Copy files";
        ScopeValue = ticked.Count > 0 ? ScopeTicked : ScopeAll;

        var naming = last?.Naming ?? new CopyNaming { Prefix = string.Empty, Digits = 8, StartNumber = 1 };
        Destination = last?.Destination ?? string.Empty;
        NamingValue = naming.Mode == CopyNamingMode.Sequential ? NamingSequential : NamingPreserve;
        Prefix = naming.Prefix;
        Digits = naming.Digits;
        StartNumber = (int)Math.Min(int.MaxValue, naming.StartNumber);
        TemplateValue = last?.TemplateName ?? CopyCommandTemplate.Copy.Name;
        Command = last?.Command ?? CopyCommandTemplate.Copy.Command;
        PreserveMetadata = last?.PreserveMetadata ?? true;
        Verify = last?.Verify ?? false;

        var config = session.Config;
        var stem = SafeFileName($"{config.ClientCode}_{config.MatterCode}_{(mode == CopyDialogMode.Batch ? "CopyBatch" : "Copy")}_{now.ToLocalTime():yyyyMMdd_HHmm}");
        var folder = settings.Current.ResolveExportFolder();
        BatchPath = Path.Combine(folder, stem + ".bat");
        SetManifest(Path.Combine(folder, stem + "_manifest.csv"));
        PropertyChanged += OnInputChanged;
        UpdateEstimate();
    }

    public CopyDialogMode Mode { get; }

    public bool IsBatch => Mode == CopyDialogMode.Batch;

    public string AllResultsText { get; }

    public IReadOnlyList<(string Value, string Label)> ScopeOptions =>
    [
        (ScopeTicked, $"Ticked rows ({(_ticked.Count == 1 ? "1 file" : _ticked.Count.ToString("N0", CultureInfo.CurrentCulture) + " files")})"),
        (ScopeAll, "All results" + (AllResultsText.Length == 0 ? " (all files)" : $" ({AllResultsText})")),
    ];

    public bool HasTicked => _ticked.Count > 0;

    [ObservableProperty]
    public partial string ScopeValue { get; set; }

    [ObservableProperty]
    public partial string Destination { get; set; }

    public static IReadOnlyList<(string Value, string Label)> NamingOptions { get; } =
    [
        (NamingPreserve, "Keep the original folders and names"),
        (NamingSequential, "Sequential names (prefix + number)"),
    ];

    [ObservableProperty]
    public partial string NamingValue { get; set; }

    public bool IsSequential => NamingValue == NamingSequential;

    [ObservableProperty]
    public partial string Prefix { get; set; }

    [ObservableProperty]
    public partial int Digits { get; set; }

    [ObservableProperty]
    public partial int StartNumber { get; set; }

    /// <summary>"Destination\MED001\folder\name.pdf" or "First file: DOC00000001.pdf, then DOC00000002.msg, …".</summary>
    public string NamingExample => IsSequential
        ? $"First file: {Naming.SequentialName(Naming.StartNumber, "pdf")}, then {Naming.SequentialName(Naming.StartNumber + 1, "msg")}, … in media, folder, name order, all in the destination folder. Files without an extension get no dot."
        : @"Each file goes to destination\Media ID\folders\name, as under the root.";

    public static IReadOnlyList<(string Value, string Label)> TemplateOptions { get; } =
    [
        (CopyCommandTemplate.Copy.Name, "copy"),
        (CopyCommandTemplate.Robocopy.Name, "robocopy (keeps the original names only)"),
        (CustomTemplate, "Custom"),
    ];

    [ObservableProperty]
    public partial string TemplateValue { get; set; }

    [ObservableProperty]
    public partial string Command { get; set; }

    public static string PlaceholderHint =>
        "{source} full path of the file · {destination} full path of the copy · {sourcedir} {sourcename} · {destdir} {destname}. Paths are quoted for you.";

    [ObservableProperty]
    public partial string BatchPath { get; set; }

    [ObservableProperty]
    public partial string ManifestPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PreserveMetadata { get; set; }

    [ObservableProperty]
    public partial bool Verify { get; set; }

    [ObservableProperty]
    public partial string EstimateText { get; private set; } = "Counting files…";

    /// <summary>What is wrong with the choices; shown once the user tried to go ahead.</summary>
    public string ValidationError => _tried ? Validate() : string.Empty;

    /// <summary>The robocopy + sequential names conflict, shown straight away (not only after trying).</summary>
    public string TemplateConflict => IsBatch && IsSequential && CurrentTemplate.IsRobocopy ? CurrentTemplate.Validate(CopyNamingMode.Sequential) ?? string.Empty : string.Empty;

    public string GoText => IsBatch ? "Write batch file" : "Copy files";

    public CopyNaming Naming => IsSequential
        ? new CopyNaming { Mode = CopyNamingMode.Sequential, Prefix = Prefix.Trim(), Digits = Digits, StartNumber = StartNumber }
        : new CopyNaming { Mode = CopyNamingMode.PreserveStructure, Prefix = Prefix.Trim(), Digits = Digits, StartNumber = StartNumber };

    public CopyCommandTemplate CurrentTemplate => new(TemplateValue, Command.Trim());

    /// <summary>Set when the dialog closed with the go button.</summary>
    public CopyRequest? Request { get; private set; }

    public CopyDialogChoices Choices => new(Destination.Trim(), Naming, TemplateValue, Command, PreserveMetadata, Verify);

    public CopyFileOptions Options => new(PreserveMetadata, Verify);

    [RelayCommand]
    private void BrowseDestination()
    {
        var picked = _dialogs.PickFolder("Copy to", Directory.Exists(Destination) ? Destination : null);
        if (picked is not null)
        {
            Destination = picked;
        }
    }

    [RelayCommand]
    private void BrowseBatch()
    {
        var folder = Path.GetDirectoryName(BatchPath);
        var picked = _dialogs.PickSaveFile("Save copy batch", "Batch file (*.bat)|*.bat", Path.GetFileName(BatchPath), Directory.Exists(folder) ? folder : null);
        if (picked is not null)
        {
            BatchPath = picked;
        }
    }

    [RelayCommand]
    private void BrowseManifest()
    {
        var folder = Path.GetDirectoryName(ManifestPath);
        var picked = _dialogs.PickSaveFile("Save manifest", "CSV file (*.csv)|*.csv", Path.GetFileName(ManifestPath), Directory.Exists(folder) ? folder : null);
        if (picked is not null)
        {
            ManifestPath = picked;
        }
    }

    [RelayCommand]
    private void Go()
    {
        _tried = true;
        OnPropertyChanged(nameof(ValidationError));
        if (Validate().Length > 0)
        {
            return;
        }

        var existing = (IsBatch ? new[] { BatchPath.Trim(), ManifestPath.Trim() } : [ManifestPath.Trim()]).Where(File.Exists).ToList();
        if (existing.Count > 0 && !_dialogs.Confirm("Replace files?", string.Join("\n", existing) + (existing.Count == 1 ? "\nalready exists. Replace it?" : "\nalready exist. Replace them?")))
        {
            return;
        }

        _estimateCancel?.Cancel();
        Request = new CopyRequest(Filter(), Destination.Trim(), Naming, ManifestPath.Trim()) { ScopeText = ScopeText() };
        Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _estimateCancel?.Cancel();
        Close(false);
    }

    internal string Validate()
    {
        var root = _session.Config.RootPath;
        if (ScopeValue == ScopeTicked && _ticked.Count == 0)
        {
            return "No rows are ticked.";
        }

        if (_fileCount == 0)
        {
            return "There are no files to copy.";
        }

        if (CopyPaths.ValidateDestination(Destination.Trim(), root) is { } destination)
        {
            return destination;
        }

        if (Naming.Validate(_fileCount ?? 1) is { } naming)
        {
            return naming;
        }

        if (IsBatch)
        {
            if (CurrentTemplate.Validate(Naming.Mode) is { } command)
            {
                return command;
            }

            if (!BatchPath.Trim().EndsWith(".bat", StringComparison.OrdinalIgnoreCase) && !BatchPath.Trim().EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return "The batch file name must end in .bat or .cmd.";
            }

            if (CopyService.ValidateOutput(BatchPath.Trim(), root, "batch file") is { } batch)
            {
                return batch;
            }
        }

        return CopyService.ValidateOutput(ManifestPath.Trim(), root, "manifest") ?? string.Empty;
    }

    private FileFilter Filter() => ScopeValue == ScopeTicked ? FileFilter.None with { FileIds = [.. _ticked] } : _allResults;

    private string ScopeText() => ScopeValue == ScopeTicked
        ? $"{_ticked.Count.ToString("N0", CultureInfo.InvariantCulture)} ticked files"
        : "All results" + (AllResultsText.Length == 0 ? string.Empty : ": " + AllResultsText);

    private void SetManifest(string path)
    {
        _settingManifest = true;
        ManifestPath = path;
        _settingManifest = false;
    }

    partial void OnManifestPathChanged(string value)
    {
        if (!_settingManifest)
        {
            _manifestEdited = true;
        }
    }

    partial void OnBatchPathChanged(string value)
    {
        // The manifest follows the batch file until the user changes it.
        if (!_manifestEdited && value.Trim().Length > 0)
        {
            var path = value.Trim();
            SetManifest(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "_manifest.csv"));
        }
    }

    partial void OnTemplateValueChanged(string value)
    {
        if (CopyCommandTemplate.Presets.FirstOrDefault(p => p.Name == value) is { } preset)
        {
            _settingCommand = true;
            Command = preset.Command;
            _settingCommand = false;
        }
    }

    partial void OnCommandChanged(string value)
    {
        if (!_settingCommand && CopyCommandTemplate.Presets.All(p => p.Command != value.Trim()))
        {
            TemplateValue = CustomTemplate;
        }
    }

    private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EstimateText) or nameof(ValidationError) or nameof(Title) or nameof(IsSequential) or nameof(NamingExample)
            or nameof(TemplateConflict))
        {
            return;
        }

        OnPropertyChanged(nameof(IsSequential));
        OnPropertyChanged(nameof(NamingExample));
        OnPropertyChanged(nameof(TemplateConflict));
        OnPropertyChanged(nameof(ValidationError));
        if (e.PropertyName == nameof(ScopeValue))
        {
            UpdateEstimate();
        }
    }

    /// <summary>Counts the files in the background (a filtered count over millions of files can take a moment).</summary>
    private void UpdateEstimate()
    {
        _estimateCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _estimateCancel = cancel;
        var filter = Filter();
        _fileCount = null;
        EstimateText = "Counting files…";
        _ = Task.Run(() =>
        {
            try
            {
                var totals = _copy.Estimate(_session, filter, cancel.Token);
                _ui.Post(() =>
                {
                    if (!cancel.IsCancellationRequested)
                    {
                        _fileCount = totals.FileCount;
                        EstimateText = Describe(totals, _unit, IsBatch);
                        OnPropertyChanged(nameof(ValidationError));
                    }
                });
            }
            catch (Exception ex) when (ex is OperationCanceledException || cancel.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Counting the files to copy failed");
                _ui.Post(() => EstimateText = "The file count is not available.");
            }
        }, CancellationToken.None);
    }

    internal static string Describe(FileTotals totals, SizeUnitSystem unit, bool batch)
    {
        var culture = CultureInfo.CurrentCulture;
        var files = totals.FileCount == 1 ? "1 file" : $"{totals.FileCount.ToString("N0", culture)} files";
        return totals.FileCount == 0
            ? "No files match."
            : $"{files}, {SizeFormatter.Format(totals.TotalBytes, unit, provider: culture)}. " +
              (batch ? "Files already at the destination are skipped when the batch runs." : "Files already at the destination are skipped and reported.");
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Export;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.ViewModels;

/// <summary>Export to Excel dialog (requirements EXP-01…EXP-04, section 8.15).</summary>
public sealed partial class ExportViewModel : DialogViewModelBase
{
    public const string ScopeAll = "all";
    public const string ScopeSelected = "selected";
    public const string ScopeView = "view";

    private readonly InventorySession _session;
    private readonly ExportService _export;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly ILogger _logger;
    private readonly FileFilter? _filesView;
    private CancellationTokenSource? _estimateCancel;
    private bool _triedExport;

    /// <param name="media">Every media in the inventory (key and Media ID), in display order.</param>
    /// <param name="preselected">Media to tick and start with the "Selected media" scope (e.g. from the Media screen).</param>
    /// <param name="filesView">The Files screen's current filter, offered as the "Current Files view" scope.</param>
    /// <param name="filesViewText">What the Files view shows, e.g. ".msg · Media 123-123_001".</param>
    public ExportViewModel(InventorySession session, ExportService export, ISettingsService settings, IDialogService dialogs,
        IUiDispatcher ui, ILogger logger, IEnumerable<(long Key, string MediaId)> media, DateTimeOffset now,
        IReadOnlyCollection<long>? preselected = null, FileFilter? filesView = null, string? filesViewText = null)
    {
        _session = session;
        _export = export;
        _dialogs = dialogs;
        _ui = ui;
        _logger = logger;
        _filesView = filesView;
        Title = "Export to Excel";
        SizeUnit = settings.Current.SizeUnit;
        OneWorkbookPerMedia = settings.Current.SplitExportPerMedia;
        FilesViewText = filesViewText ?? string.Empty;
        foreach (var (key, id) in media)
        {
            var choice = new ExportMediaChoice(key, id, preselected?.Contains(key) ?? false);
            choice.PropertyChanged += OnInputChanged;
            Media.Add(choice);
        }

        ScopeValue = filesView is not null ? ScopeView : preselected is { Count: > 0 } ? ScopeSelected : ScopeAll;
        var config = session.Config;
        var fileName = SafeFileName($"{config.ClientCode}_{config.MatterCode}_Inventory_{now.ToLocalTime():yyyyMMdd}.xlsx");
        OutputPath = Path.Combine(settings.Current.ResolveExportFolder(), fileName);
        PropertyChanged += OnInputChanged;
        UpdateEstimate();
    }

    public IReadOnlyList<(string Value, string Label)> ScopeOptions =>
        _filesView is null
            ? [(ScopeAll, "All media"), (ScopeSelected, "Selected media")]
            : [(ScopeAll, "All media"), (ScopeSelected, "Selected media"), (ScopeView, "Current Files view")];

    [ObservableProperty]
    public partial string ScopeValue { get; set; }

    public ObservableCollection<ExportMediaChoice> Media { get; } = [];

    public bool IsSelectedScope => ScopeValue == ScopeSelected;

    public bool IsViewScope => ScopeValue == ScopeView;

    public string FilesViewText { get; }

    [ObservableProperty]
    public partial bool SummarySheet { get; set; } = true;

    [ObservableProperty]
    public partial bool MediaSheet { get; set; } = true;

    [ObservableProperty]
    public partial bool CategoriesSheet { get; set; } = true;

    [ObservableProperty]
    public partial bool ExtensionsSheet { get; set; } = true;

    [ObservableProperty]
    public partial bool FilesSheet { get; set; } = true;

    [ObservableProperty]
    public partial bool ErrorsSheet { get; set; } = true;

    public SizeUnitSystem SizeUnit { get; }

    public string UnitsText => SizeUnit == SizeUnitSystem.Binary
        ? "Size (bytes) and Size (MiB), 1 MiB = 1,048,576 bytes (binary units in Settings)"
        : "Size (bytes) and Size (MB), 1 MB = 1,000,000 bytes (decimal units in Settings)";

    [ObservableProperty]
    public partial bool OneWorkbookPerMedia { get; set; }

    [ObservableProperty]
    public partial string OutputPath { get; set; }

    [ObservableProperty]
    public partial string EstimateText { get; private set; } = "Counting rows…";

    /// <summary>What is wrong with the choices; shown once the user tried to export.</summary>
    public string ValidationError => _triedExport ? Validate() : string.Empty;

    /// <summary>The export to run, set when the dialog closed with Export.</summary>
    public ExportRequest? Request { get; private set; }

    [RelayCommand]
    private void Browse()
    {
        var folder = Path.GetDirectoryName(OutputPath);
        var picked = _dialogs.PickSaveFile("Export to Excel", "Excel workbook (*.xlsx)|*.xlsx", Path.GetFileName(OutputPath),
            Directory.Exists(folder) ? folder : null);
        if (picked is not null)
        {
            OutputPath = picked;
        }
    }

    [RelayCommand]
    private void Export()
    {
        _triedExport = true;
        OnPropertyChanged(nameof(ValidationError));
        if (Validate().Length > 0)
        {
            return;
        }

        var request = BuildRequest();
        var existing = (request.OneWorkbookPerMedia
                ? SelectedKeys().Select(k => ExportService.PerMediaPath(request.OutputPath, Media.First(m => m.Key == k).MediaId))
                : [request.OutputPath])
            .Where(File.Exists)
            .ToList();
        if (existing.Count > 0 && !_dialogs.Confirm("Replace files?",
                (existing.Count == 1 ? $"{existing[0]} already exists." : $"{existing.Count} of the workbooks already exist.") +
                " Do you want to replace " + (existing.Count == 1 ? "it?" : "them?")))
        {
            return;
        }

        _estimateCancel?.Cancel();
        Request = request;
        Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _estimateCancel?.Cancel();
        Close(false);
    }

    private string Validate()
    {
        if (!(SummarySheet || MediaSheet || CategoriesSheet || ExtensionsSheet || FilesSheet || ErrorsSheet))
        {
            return "Choose at least one sheet.";
        }

        if (IsSelectedScope && !Media.Any(m => m.IsChecked))
        {
            return "Tick at least one media to export.";
        }

        var path = OutputPath.Trim();
        if (path.Length == 0 || !Path.IsPathFullyQualified(path) || !path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Enter the full path of the workbook, ending in .xlsx.";
        }

        return string.Empty;
    }

    private ExportRequest BuildRequest()
    {
        var sheets = new HashSet<ExportSheet>();
        void Add(bool on, ExportSheet sheet)
        {
            if (on)
            {
                sheets.Add(sheet);
            }
        }

        Add(SummarySheet, ExportSheet.Summary);
        Add(MediaSheet, ExportSheet.Media);
        Add(CategoriesSheet, ExportSheet.Categories);
        Add(ExtensionsSheet, ExportSheet.Extensions);
        Add(FilesSheet, ExportSheet.Files);
        Add(ErrorsSheet, ExportSheet.Errors);
        return new ExportRequest
        {
            OutputPath = OutputPath.Trim(),
            MediaKeys = IsSelectedScope ? SelectedKeys() : null,
            FilesView = IsViewScope ? _filesView : null,
            Sheets = sheets,
            SizeUnit = SizeUnit,
            OneWorkbookPerMedia = OneWorkbookPerMedia,
        };
    }

    private List<long> SelectedKeys() => IsSelectedScope ? [.. Media.Where(m => m.IsChecked).Select(m => m.Key)] : [.. Media.Select(m => m.Key)];

    private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EstimateText) or nameof(ValidationError) or nameof(Title) or nameof(IsSelectedScope) or nameof(IsViewScope))
        {
            return;
        }

        if (e.PropertyName == nameof(ScopeValue))
        {
            OnPropertyChanged(nameof(IsSelectedScope));
            OnPropertyChanged(nameof(IsViewScope));
        }

        OnPropertyChanged(nameof(ValidationError));
        if (e.PropertyName is not nameof(OutputPath))
        {
            UpdateEstimate();
        }
    }

    /// <summary>Counts the rows in the background (a filtered count over millions of files can take a moment).</summary>
    private void UpdateEstimate()
    {
        _estimateCancel?.Cancel();
        if (IsSelectedScope && !Media.Any(m => m.IsChecked))
        {
            EstimateText = "No media selected.";
            return;
        }

        var cancel = new CancellationTokenSource();
        _estimateCancel = cancel;
        var request = BuildRequest();
        EstimateText = "Counting rows…";
        _ = Task.Run(() =>
        {
            try
            {
                var estimate = _export.Estimate(_session, request, cancel.Token);
                if (!cancel.IsCancellationRequested)
                {
                    _ui.Post(() =>
                    {
                        if (!cancel.IsCancellationRequested)
                        {
                            EstimateText = Describe(estimate, request);
                        }
                    });
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || cancel.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Estimating the export failed");
                _ui.Post(() => EstimateText = "The row count is not available.");
            }
        }, CancellationToken.None);
    }

    internal static string Describe(ExportEstimate estimate, ExportRequest request)
    {
        var culture = CultureInfo.CurrentCulture;
        var text = $"Estimated rows: {estimate.TotalRows.ToString("N0", culture)}";
        if (request.Sheets.Contains(ExportSheet.Files))
        {
            text += estimate.FilesSheets == 1 ? " → 1 Files sheet" : $" → {estimate.FilesSheets.ToString("N0", culture)} Files sheets";
        }

        if (estimate.Workbooks > 1)
        {
            text += $" in {estimate.Workbooks.ToString("N0", culture)} workbooks";
        }

        return text + ".";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
    }
}

/// <summary>A media the export can include.</summary>
public sealed partial class ExportMediaChoice(long key, string mediaId, bool isChecked) : ObservableObject
{
    public long Key { get; } = key;

    public string MediaId { get; } = mediaId;

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

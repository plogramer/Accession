using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Scanning;
using Accession.Core.Settings;
using Accession.Data.Repositories;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Platform;
using Accession.Presentation.Services;
using Accession.UI.Components;
using Accession.UI.FilesScreen;
using Accession.UI.ScanScreens;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using Microsoft.Extensions.Logging;
using Accession.UI.Shell;

namespace Accession.Presentation.ViewModels.Scanning;

/// <summary>Errors screen (requirements SCN-53, section 8.11).</summary>
public sealed partial class ErrorsViewModel : ViewModelBase, IErrorsModel, IDisposable
{
    private readonly IDesktop _desktop;

    public const int PageSize = 500;

    private readonly InventoryHost _host;
    private readonly ScanHost _scans;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ErrorsViewModel> _logger;
    private Dictionary<long, string> _mediaIds = [];
    private bool _loading;
    private readonly ToastService _toasts;
    private readonly Dictionary<int, long?> _pageStarts = new() { [0] = null };

    public ErrorsViewModel(InventoryHost host, ScanHost scans, ISettingsService settings, IDialogService dialogs, ILogger<ErrorsViewModel> logger, IDesktop desktop,
        ToastService toasts)
    {
        _toasts = toasts;
        _desktop = desktop;
        _host = host;
        _scans = scans;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        ErrorTypeOptions = [new Option<ScanErrorType?>(null, "All error types"), .. Enum.GetValues<ScanErrorType>().Select(t => new Option<ScanErrorType?>(t, Humanize(t)))];
        SelectedErrorType = ErrorTypeOptions[0];
        _host.MediaChanged += OnMediaChanged;
        _scans.PropertyChanged += OnScansChanged;
        Reload();
    }

    public ObservableCollection<Option<long?>> MediaOptions { get; } = [];

    public IReadOnlyList<Option<ScanErrorType?>> ErrorTypeOptions { get; }

    [ObservableProperty]
    public partial Option<long?>? SelectedMedia { get; set; }

    [ObservableProperty]
    public partial Option<ScanErrorType?> SelectedErrorType { get; set; }

    /// <summary>Show Info entries (e.g. skipped reparse points); hidden by default.</summary>
    [ObservableProperty]
    public partial bool ShowInfo { get; set; }

    public ObservableCollection<ErrorRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand), nameof(RetryFailedCommand), nameof(OpenContainingFolderCommand))]
    public partial ErrorRow? SelectedRow { get; set; }

    // ---- Numbered pages (web UI) ----

    public IReadOnlyList<int> PageSizes { get; } = [500, 1_000, 5_000, 10_000];

    [ObservableProperty]
    public partial int ErrorsPageSize { get; private set; } = PageSize;

    [ObservableProperty]
    public partial int PageIndex { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ErrorRow> PageRows { get; private set; } = [];

    [ObservableProperty]
    public partial long TotalCount { get; private set; }

    public IReadOnlyList<SelectOption> MediaFilterOptions =>
        [.. MediaOptions.Select(o => new SelectOption(o.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, o.Label))];

    public string MediaFilterValue
    {
        get => SelectedMedia?.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        set => SelectedMedia = MediaOptions.FirstOrDefault(o => (o.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) == value) ?? MediaOptions.FirstOrDefault();
    }

    public IReadOnlyList<SelectOption> ErrorTypeFilterOptions =>
        [.. ErrorTypeOptions.Select(o => new SelectOption(o.Value?.ToString() ?? string.Empty, o.Label))];

    public string ErrorTypeFilterValue
    {
        get => SelectedErrorType?.Value?.ToString() ?? string.Empty;
        set => SelectedErrorType = ErrorTypeOptions.FirstOrDefault(o => (o.Value?.ToString() ?? string.Empty) == value) ?? ErrorTypeOptions[0];
    }

    ICommand IErrorsModel.CopyPathCommand => CopyPathCommand;

    ICommand IErrorsModel.OpenContainingFolderCommand => OpenContainingFolderCommand;

    ICommand IErrorsModel.RetryFailedCommand => RetryFailedCommand;

    ICommand IRefreshableScreen.RefreshCommand => RefreshCommand;

    public Task GoToPageAsync(int pageIndex)
    {
        LoadNumberedPage(Math.Clamp(pageIndex, 0, Math.Max(0, (int)((TotalCount - 1) / ErrorsPageSize))));
        return Task.CompletedTask;
    }

    public Task SetPageSizeAsync(int pageSize)
    {
        if (PageSizes.Contains(pageSize) && pageSize != ErrorsPageSize)
        {
            ErrorsPageSize = pageSize;
            ResetNumberedPages();
        }

        return Task.CompletedTask;
    }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Errors (excluding Info) across active media, for the navigation badge.</summary>
    [ObservableProperty]
    public partial long ErrorCount { get; set; }

    public bool HasRows => Rows.Count > 0;

    public void Dispose()
    {
        _host.MediaChanged -= OnMediaChanged;
        _scans.PropertyChanged -= OnScansChanged;
    }

    partial void OnSelectedMediaChanged(Option<long?>? value)
    {
        OnPropertyChanged(nameof(MediaFilterValue));
        LoadFirstPage();
    }

    partial void OnSelectedErrorTypeChanged(Option<ScanErrorType?> value)
    {
        OnPropertyChanged(nameof(ErrorTypeFilterValue));
        LoadFirstPage();
    }

    partial void OnShowInfoChanged(bool value) => LoadFirstPage();

    [RelayCommand]
    private void Refresh() => Reload();

    [RelayCommand]
    private void LoadMore() => LoadPage(append: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedRow is { } row && _host.Config is { } config)
        {
            _desktop.SetClipboardText(ScanPaths.ToFullPath(config.RootPath, row.RelativePath));
            _toasts.Show("Path copied", ToastKind.Success);
        }
    }

    /// <summary>Opens Explorer at the item (or its folder). Files are never opened (BRW-04).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenContainingFolder()
    {
        if (SelectedRow is not { } row || _host.Config is not { } config)
        {
            return;
        }

        var path = ScanPaths.ToFullPath(config.RootPath, row.RelativePath);
        if (File.Exists(path))
        {
            _desktop.SelectInExplorer(path);
        }
        else if (Directory.Exists(path))
        {
            _desktop.OpenFolder(path);
        }
        else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
        {
            _desktop.OpenFolder(folder);
        }
        else
        {
            _toasts.Show($"'{path}' cannot be found.", ToastKind.Warning);
        }
    }

    /// <summary>Hash again the failed files of the filtered media (or of the selected error's media).</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void RetryFailed()
    {
        var mediaKey = SelectedMedia?.Value ?? SelectedRow?.MediaKey;
        if (mediaKey is null || _host.Session is not { } session)
        {
            return;
        }

        Media? media;
        using (var scope = session.Database.Open())
        {
            media = new MediaRepository(scope).Get(mediaKey.Value);
        }

        if (media is not null)
        {
            _scans.Enqueue([(media, ScanType.RetryFailed)]);
        }
    }

    private bool HasSelection() => SelectedRow is not null;

    private bool CanRetry() => _scans.CanScan && (SelectedMedia?.Value ?? SelectedRow?.MediaKey) is not null;

    private void OnMediaChanged(object? sender, EventArgs e) => Reload();

    private void OnScansChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RetryFailedCommand.NotifyCanExecuteChanged();

    private void Reload()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        _loading = true;
        try
        {
            var selectedKey = SelectedMedia?.Value;
            using (var scope = session.Database.Open())
            {
                var media = new MediaRepository(scope).ListActive();
                _mediaIds = media.ToDictionary(m => m.MediaKey, m => m.MediaId);
                ErrorCount = media.Sum(m => m.ErrorCount);
            }

            MediaOptions.Clear();
            MediaOptions.Add(new Option<long?>(null, "All media"));
            foreach (var (key, id) in _mediaIds.OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase))
            {
                MediaOptions.Add(new Option<long?>(key, id));
            }

            SelectedMedia = MediaOptions.FirstOrDefault(o => o.Value == selectedKey) ?? MediaOptions[0];
            OnPropertyChanged(nameof(MediaFilterOptions));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading media for the Errors screen failed");
        }
        finally
        {
            _loading = false;
        }

        LoadFirstPage();
    }

    private void LoadFirstPage()
    {
        if (!_loading)
        {
            LoadPage(append: false);
        }
    }

    private void LoadPage(bool append)
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        if (!append)
        {
            Rows.Clear();
        }

        var severities = ShowInfo ? null : new[] { ScanErrorSeverity.Error, ScanErrorSeverity.Warning };
        var types = SelectedErrorType?.Value is { } type ? new[] { type } : null;
        var query = new ScanErrorQuery
        {
            MediaKey = SelectedMedia?.Value,
            ErrorTypes = types,
            Severities = severities,
            AfterErrorId = append && Rows.Count > 0 ? Rows[^1].ErrorId : null,
            PageSize = PageSize,
        };

        try
        {
            var zone = _settings.Current.DisplayTimeZone;
            using var scope = session.Database.Open();
            var page = new ScanErrorRepository(scope).List(query);
            foreach (var e in page)
            {
                Rows.Add(ToRow(e, zone));
            }

            HasMore = page.Count == PageSize;
            var total = CountMatching(scope, query);
            Summary = total == 0 ? "No errors match the filter." : $"{Rows.Count:N0} of {total:N0} shown";
            if (!append)
            {
                TotalCount = total;
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading errors failed");
        }

        OnPropertyChanged(nameof(HasRows));
        if (!append)
        {
            ResetNumberedPages();
        }
    }

    private ScanErrorQuery CurrentQuery() => new()
    {
        MediaKey = SelectedMedia?.Value,
        ErrorTypes = SelectedErrorType?.Value is { } type ? [type] : null,
        Severities = ShowInfo ? null : [ScanErrorSeverity.Error, ScanErrorSeverity.Warning],
        PageSize = ErrorsPageSize,
    };

    private void ResetNumberedPages()
    {
        _pageStarts.Clear();
        _pageStarts[0] = null;
        LoadNumberedPage(0);
    }

    /// <summary>Reads one numbered page: keyset from a known page start, else OFFSET.</summary>
    private void LoadNumberedPage(int pageIndex)
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        var query = _pageStarts.TryGetValue(pageIndex, out var after)
            ? CurrentQuery() with { AfterErrorId = after }
            : CurrentQuery() with { Offset = (long)pageIndex * ErrorsPageSize };
        try
        {
            var zone = _settings.Current.DisplayTimeZone;
            using var scope = session.Database.Open();
            var page = new ScanErrorRepository(scope).List(query);
            if (page.Count == ErrorsPageSize)
            {
                _pageStarts[pageIndex + 1] = page[^1].ErrorId;
            }

            PageRows = [.. page.Select(e => ToRow(e, zone))];
            PageIndex = pageIndex;
            SelectedRow = null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading errors failed");
        }
    }

    private ErrorRow ToRow(ScanErrorEntry e, DisplayTimeZone zone) => new(
        e.ErrorId,
        e.MediaKey,
        TimeFormatter.Format(e.OccurredAtUtc, zone),
        _mediaIds.GetValueOrDefault(e.MediaKey, string.Empty),
        e.RelativePath,
        e.ItemType.ToString(),
        Humanize(e.ErrorType),
        e.Severity.ToString(),
        e.ErrorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        e.Message ?? string.Empty);

    private static long CountMatching(Accession.Data.DbScope scope, ScanErrorQuery query)
    {
        var sql = "SELECT COUNT(*) FROM ScanError e JOIN Media m ON m.MediaKey = e.MediaKey AND m.IsDeleted = 0 WHERE 1 = 1";
        var parameters = new DynamicParameters();
        if (query.MediaKey is { } key)
        {
            sql += " AND e.MediaKey = @key";
            parameters.Add("key", key);
        }

        if (query.ErrorTypes is { Count: > 0 } types)
        {
            sql += " AND e.ErrorType IN @types";
            parameters.Add("types", types.Select(t => t.ToString()).ToList());
        }

        if (query.Severities is { Count: > 0 } severities)
        {
            sql += " AND e.Severity IN @severities";
            parameters.Add("severities", severities.Select(s => s.ToString()).ToList());
        }

        return scope.Connection.ExecuteScalar<long>(sql, parameters);
    }

    private static string Humanize(ScanErrorType type) => type switch
    {
        ScanErrorType.AccessDenied => "Access denied",
        ScanErrorType.FileLocked => "File locked",
        ScanErrorType.NotFound => "Not found",
        ScanErrorType.PathError => "Path error",
        ScanErrorType.IOError => "I/O error",
        ScanErrorType.ChangedDuringScan => "Changed during scan",
        ScanErrorType.ReparsePointSkipped => "Reparse point skipped",
        _ => "Other",
    };
}

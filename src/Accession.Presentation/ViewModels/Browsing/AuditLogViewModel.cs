using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Data.Audit;
using Accession.Presentation.Mvvm;
using Accession.Presentation.Services;
using Accession.UI.FilesScreen;
using Accession.UI.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Accession.UI.Shell;

namespace Accession.Presentation.ViewModels.Browsing;

/// <summary>Audit Log screen (requirement AUD-04, section 8.13). Read-only.</summary>
public sealed partial class AuditLogViewModel : ViewModelBase, IAuditModel, IDisposable
{
    public const int PageSize = 500;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly InventoryHost _host;
    private readonly ISettingsService _settings;
    private readonly ILogger<AuditLogViewModel> _logger;
    private bool _loading;
    private readonly Dictionary<int, long?> _pageStarts = new() { [0] = null };

    public AuditLogViewModel(InventoryHost host, ISettingsService settings, ILogger<AuditLogViewModel> logger)
    {
        _host = host;
        _settings = settings;
        _logger = logger;
        ActionOptions = [new Option<AuditAction?>(null, "All actions"), .. Enum.GetValues<AuditAction>().Select(a => new Option<AuditAction?>(a, a.ToString()))];
        SelectedAction = ActionOptions[0];
        _host.MediaChanged += OnChanged;
        LoadUsers();
        LoadPage(append: false);
    }

    public IReadOnlyList<Option<AuditAction?>> ActionOptions { get; }

    [ObservableProperty]
    public partial Option<AuditAction?> SelectedAction { get; set; }

    public ObservableCollection<Option<string?>> UserOptions { get; } = [];

    [ObservableProperty]
    public partial Option<string?>? SelectedUser { get; set; }

    [ObservableProperty]
    public partial string MediaIdText { get; set; } = string.Empty;

    /// <summary>Local calendar date (inclusive).</summary>
    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    /// <summary>Local calendar date (inclusive).</summary>
    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    public ObservableCollection<AuditRowVm> Rows { get; } = [];

    [ObservableProperty]
    public partial AuditRowVm? SelectedRow { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    public string Details => SelectedRow?.Details is { Length: > 0 } json ? Pretty(json) : string.Empty;

    // ---- Numbered pages and string filters (web UI) ----

    public IReadOnlyList<int> PageSizes { get; } = [500, 1_000, 5_000, 10_000];

    [ObservableProperty]
    public partial int AuditPageSize { get; private set; } = PageSize;

    [ObservableProperty]
    public partial int PageIndex { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<AuditRowVm> PageRows { get; private set; } = [];

    [ObservableProperty]
    public partial long TotalCount { get; private set; }

    public IReadOnlyList<SelectOption> ActionFilterOptions =>
        [.. ActionOptions.Select(o => new SelectOption(o.Value?.ToString() ?? string.Empty, o.Value is null ? o.Label : Words(o.Label)))];

    public string ActionFilterValue
    {
        get => SelectedAction?.Value?.ToString() ?? string.Empty;
        set => SelectedAction = ActionOptions.FirstOrDefault(o => (o.Value?.ToString() ?? string.Empty) == value) ?? ActionOptions[0];
    }

    public IReadOnlyList<SelectOption> UserFilterOptions => [.. UserOptions.Select(o => new SelectOption(o.Value ?? string.Empty, o.Label))];

    public string UserFilterValue
    {
        get => SelectedUser?.Value ?? string.Empty;
        set => SelectedUser = UserOptions.FirstOrDefault(o => (o.Value ?? string.Empty) == value) ?? UserOptions.FirstOrDefault();
    }

    public string FromDateText
    {
        get => FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        set => FromDate = ParseDate(value);
    }

    public string ToDateText
    {
        get => ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        set => ToDate = ParseDate(value);
    }

    ICommand IAuditModel.ApplyMediaFilterCommand => ApplyMediaFilterCommand;

    ICommand IAuditModel.ClearFiltersCommand => ClearFiltersCommand;

    ICommand IRefreshableScreen.RefreshCommand => RefreshCommand;

    public Task GoToPageAsync(int pageIndex)
    {
        LoadNumberedPage(Math.Clamp(pageIndex, 0, Math.Max(0, (int)((TotalCount - 1) / AuditPageSize))));
        return Task.CompletedTask;
    }

    public Task SetPageSizeAsync(int pageSize)
    {
        if (PageSizes.Contains(pageSize) && pageSize != AuditPageSize)
        {
            AuditPageSize = pageSize;
            ResetNumberedPages();
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _host.MediaChanged -= OnChanged;

    partial void OnSelectedRowChanged(AuditRowVm? value) => OnPropertyChanged(nameof(Details));

    partial void OnSelectedActionChanged(Option<AuditAction?> value)
    {
        OnPropertyChanged(nameof(ActionFilterValue));
        Reload();
    }

    partial void OnSelectedUserChanged(Option<string?>? value)
    {
        OnPropertyChanged(nameof(UserFilterValue));
        Reload();
    }

    partial void OnFromDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(FromDateText));
        Reload();
    }

    partial void OnToDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ToDateText));
        Reload();
    }

    [RelayCommand]
    private void ApplyMediaFilter() => Reload();

    [RelayCommand]
    private void Refresh()
    {
        LoadUsers();
        LoadPage(append: false);
    }

    [RelayCommand]
    private void LoadMore() => LoadPage(append: true);

    [RelayCommand]
    private void ClearFilters()
    {
        _loading = true;
        SelectedAction = ActionOptions[0];
        SelectedUser = UserOptions.FirstOrDefault();
        MediaIdText = string.Empty;
        FromDate = ToDate = null;
        _loading = false;
        LoadPage(append: false);
    }

    private void Reload()
    {
        if (!_loading)
        {
            LoadPage(append: false);
        }
    }

    private void LoadUsers()
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        _loading = true;
        try
        {
            var selected = SelectedUser?.Value;
            UserOptions.Clear();
            UserOptions.Add(new Option<string?>(null, "All users"));
            foreach (var user in session.Audit.ListUserNames())
            {
                UserOptions.Add(new Option<string?>(user, user));
            }

            SelectedUser = UserOptions.FirstOrDefault(u => u.Value == selected) ?? UserOptions[0];
            OnPropertyChanged(nameof(UserFilterOptions));
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading audit users failed");
        }
        finally
        {
            _loading = false;
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

        var query = new AuditQuery
        {
            From = FromDate is { } from ? new DateTimeOffset(from.Date) : null,
            To = ToDate is { } to ? new DateTimeOffset(to.Date.AddDays(1)) : null,
            Actions = SelectedAction?.Value is { } action ? [action] : null,
            UserName = SelectedUser?.Value,
            MediaId = string.IsNullOrWhiteSpace(MediaIdText) ? null : MediaIdText.Trim(),
            BeforeAuditId = append && Rows.Count > 0 ? Rows[^1].AuditId : null,
            PageSize = PageSize,
        };

        try
        {
            var zone = _settings.Current.DisplayTimeZone;
            var page = session.Audit.Query(query);
            foreach (var entry in page)
            {
                Rows.Add(new AuditRowVm(entry.AuditId, TimeFormatter.Format(entry.OccurredAtUtc, zone), entry.UserName,
                    entry.MachineName, entry.Action.ToString(), entry.MediaId ?? string.Empty, entry.Details ?? string.Empty));
            }

            HasMore = page.Count == PageSize;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading the audit log failed");
        }

        if (!append)
        {
            ResetNumberedPages();
        }
    }

    private AuditQuery CurrentQuery() => new()
    {
        From = FromDate is { } from ? new DateTimeOffset(from.Date) : null,
        To = ToDate is { } to ? new DateTimeOffset(to.Date.AddDays(1)) : null,
        Actions = SelectedAction?.Value is { } action ? [action] : null,
        UserName = SelectedUser?.Value,
        MediaId = string.IsNullOrWhiteSpace(MediaIdText) ? null : MediaIdText.Trim(),
        PageSize = AuditPageSize,
    };

    private void ResetNumberedPages()
    {
        _pageStarts.Clear();
        _pageStarts[0] = null;
        if (_host.Session is { } session)
        {
            try
            {
                TotalCount = session.Audit.Count(CurrentQuery());
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
            {
                _logger.LogError(ex, "Counting audit entries failed");
            }
        }

        LoadNumberedPage(0);
    }

    /// <summary>One numbered page, newest first: keyset from a known page start, else OFFSET.</summary>
    private void LoadNumberedPage(int pageIndex)
    {
        if (_host.Session is not { } session)
        {
            return;
        }

        var query = _pageStarts.TryGetValue(pageIndex, out var before)
            ? CurrentQuery() with { BeforeAuditId = before }
            : CurrentQuery() with { Offset = (long)pageIndex * AuditPageSize };
        try
        {
            var zone = _settings.Current.DisplayTimeZone;
            var page = session.Audit.Query(query);
            if (page.Count == AuditPageSize)
            {
                _pageStarts[pageIndex + 1] = page[^1].AuditId;
            }

            PageRows = [.. page.Select(entry => new AuditRowVm(entry.AuditId, TimeFormatter.Format(entry.OccurredAtUtc, zone), entry.UserName,
                entry.MachineName, entry.Action.ToString(), entry.MediaId ?? string.Empty, entry.Details ?? string.Empty))];
            PageIndex = pageIndex;
            SelectedRow = null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            _logger.LogError(ex, "Loading the audit log failed");
        }
    }

    /// <summary>"MediaDeleted" → "Media deleted".</summary>
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static DateTime? ParseDate(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => LoadPage(append: false);
}

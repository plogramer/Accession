using System.Collections.ObjectModel;
using System.Text.Json;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Data.Audit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Accession.App.ViewModels.Browsing;

/// <summary>Audit Log screen (requirement AUD-04, section 8.13). Read-only.</summary>
public sealed partial class AuditLogViewModel : ViewModelBase, IDisposable
{
    public const int PageSize = 500;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly InventoryHost _host;
    private readonly ISettingsService _settings;
    private readonly ILogger<AuditLogViewModel> _logger;
    private bool _loading;

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

    public void Dispose() => _host.MediaChanged -= OnChanged;

    partial void OnSelectedRowChanged(AuditRowVm? value) => OnPropertyChanged(nameof(Details));

    partial void OnSelectedActionChanged(Option<AuditAction?> value) => Reload();

    partial void OnSelectedUserChanged(Option<string?>? value) => Reload();

    partial void OnFromDateChanged(DateTime? value) => Reload();

    partial void OnToDateChanged(DateTime? value) => Reload();

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
    }

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

public sealed record AuditRowVm(long AuditId, string Time, string User, string Machine, string Action, string MediaId, string Details);

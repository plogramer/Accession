using System.Globalization;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Formatting;
using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Core.Settings;
using Accession.Data.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>Inventory Properties dialog (requirements INV-07, INV-09, section 8.14).</summary>
public sealed partial class InventoryPropertiesViewModel : DialogViewModelBase
{
    private readonly InventoryHost _host;
    private readonly InventoryPropertiesService _properties;
    private readonly IDialogService _dialogs;
    private readonly Func<ChangeRootPathViewModel> _changeRoot;
    private readonly DisplayTimeZone _timeZone;

    public InventoryPropertiesViewModel(
        InventoryHost host,
        InventoryPropertiesService properties,
        IDialogService dialogs,
        ISettingsService settings,
        Func<ChangeRootPathViewModel> changeRoot)
    {
        _host = host;
        _properties = properties;
        _dialogs = dialogs;
        _changeRoot = changeRoot;
        _timeZone = settings.Current.DisplayTimeZone;
        Title = "Inventory Properties";

        var config = host.Config ?? throw new InvalidOperationException("No inventory is open.");
        ClientName = config.ClientName;
        ClientCode = config.ClientCode;
        MatterName = config.MatterName;
        MatterCode = config.MatterCode;
        Description = config.Description ?? string.Empty;
        MatterUrl = config.MatterUrl ?? string.Empty;
    }

    public FieldErrors Errors { get; } = new();

    public bool IsReadOnly => !_host.CanModify;

    public bool CanEdit => _host.CanModify;

    [ObservableProperty]
    public partial string ClientName { get; set; }

    [ObservableProperty]
    public partial string ClientCode { get; set; }

    [ObservableProperty]
    public partial string MatterName { get; set; }

    [ObservableProperty]
    public partial string MatterCode { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string MatterUrl { get; set; }

    // Location tab
    public string RootPath => _host.Config?.RootPath ?? string.Empty;

    public string DbPath => _host.Session?.DbPath ?? string.Empty;

    public string RootStatus => _host.IsRootAvailable ? "Reachable" : "Not available (offline)";

    // Info tab
    public string InventoryGuid => _host.Config?.InventoryGuid.ToString("D") ?? string.Empty;

    public string SchemaVersion => _host.Config?.SchemaVersion.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    public string CreatedBy => _host.Config is { } c ? $"{c.CreatedBy} on {c.CreatedOnMachine}" : string.Empty;

    public string CreatedAt => TimeFormatter.Format(_host.Config?.CreatedAtUtc, _timeZone);

    public string CreatedAppVersion => _host.Config?.CreatedAppVersion ?? string.Empty;

    public string LockHolder => _host.Session switch
    {
        null => string.Empty,
        { IsReadOnly: true, LockService: null } => "Opened read-only (another user holds the lock)",
        { IsReadOnly: true } => "Lock lost to another user (read-only)",
        _ => "You (this session)",
    };

    partial void OnClientNameChanged(string value) => Validate();

    partial void OnClientCodeChanged(string value) => Validate();

    partial void OnMatterNameChanged(string value) => Validate();

    partial void OnMatterCodeChanged(string value) => Validate();

    partial void OnMatterUrlChanged(string value) => Validate();

    [RelayCommand]
    private void ChangeRootPath()
    {
        if (_dialogs.ShowDialog(_changeRoot()) == true)
        {
            OnPropertyChanged(nameof(RootPath));
            OnPropertyChanged(nameof(RootStatus));
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_host.Session is not { } session || !_host.CanModify)
        {
            Close(false);
            return;
        }

        try
        {
            _properties.UpdateMatter(session, new MatterInfo(ClientName, ClientCode, MatterName, MatterCode, Description, MatterUrl));
            _host.Refresh();
            Close(true);
        }
        catch (InventoryValidationException ex)
        {
            Errors.Set(ex.Errors);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _dialogs.ShowError("Inventory Properties", $"The changes could not be saved.\n\n{ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private void Validate() =>
        Errors.Set(InventoryValidation.ValidateMatter(ClientName, ClientCode, MatterName, MatterCode, MatterUrl));
}

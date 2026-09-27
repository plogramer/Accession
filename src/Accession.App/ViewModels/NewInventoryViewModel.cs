using System.IO;
using Accession.App.Mvvm;
using Accession.App.Services;
using Accession.Core.Inventories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.App.ViewModels;

/// <summary>New Inventory dialog (requirements INV-01…INV-03, section 8.3).</summary>
public sealed partial class NewInventoryViewModel : DialogViewModelBase
{
    private static readonly string[] IgnoredRootFolders = ["$RECYCLE.BIN", "System Volume Information"];

    private readonly IDialogService _dialogs;
    private readonly HashSet<string> _touched = [];
    private bool _showAllErrors;
    private bool _savePathEditedByUser;
    private bool _updatingSavePath;

    public NewInventoryViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Title = "New Inventory";
    }

    public FieldErrors Errors { get; } = new();

    [ObservableProperty]
    public partial string ClientName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClientCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MatterName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MatterCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MatterUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RootFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SavePath { get; set; } = string.Empty;

    /// <summary>Show the New Media Found dialog after creating (section 8.3).</summary>
    [ObservableProperty]
    public partial bool ShowDiscoveredMedia { get; set; } = true;

    /// <summary>E.g. "12 subfolders found (candidate media)".</summary>
    [ObservableProperty]
    public partial string RootInfo { get; set; } = string.Empty;

    public CreateInventoryRequest BuildRequest() => new(
        ClientName, ClientCode, MatterName, MatterCode, Description, MatterUrl, RootFolder, SavePath);

    partial void OnClientNameChanged(string value) => Touch(InventoryFields.ClientName, pathsChanged: false);

    partial void OnClientCodeChanged(string value)
    {
        UpdateDefaultSavePath();
        Touch(InventoryFields.ClientCode, pathsChanged: false);
    }

    partial void OnMatterNameChanged(string value) => Touch(InventoryFields.MatterName, pathsChanged: false);

    partial void OnMatterCodeChanged(string value)
    {
        UpdateDefaultSavePath();
        Touch(InventoryFields.MatterCode, pathsChanged: false);
    }

    partial void OnMatterUrlChanged(string value) => Touch(InventoryFields.MatterUrl, pathsChanged: false);

    partial void OnRootFolderChanged(string value)
    {
        RootInfo = DescribeRoot(value);
        UpdateDefaultSavePath();
        Touch(InventoryFields.RootFolder, pathsChanged: true);
    }

    partial void OnSavePathChanged(string value)
    {
        if (_updatingSavePath)
        {
            // Default path follows Client ID / Matter ID as they are typed; file system checks run on Create.
            return;
        }

        _savePathEditedByUser = true;
        Touch(InventoryFields.SavePath, pathsChanged: true);
    }

    [RelayCommand]
    private void BrowseRootFolder()
    {
        var folder = _dialogs.PickFolder("Select the root folder that contains the media folders", NullIfEmpty(RootFolder));
        if (folder is not null)
        {
            RootFolder = folder;
        }
    }

    [RelayCommand]
    private void BrowseSavePath()
    {
        var folder = string.IsNullOrWhiteSpace(SavePath) ? NullIfEmpty(RootFolder) : Path.GetDirectoryName(SavePath);
        var path = _dialogs.PickSaveFile(
            "Save inventory as",
            "Accession inventory (*.sqlite)|*.sqlite",
            InventoryFileName.Default(ClientCode, MatterCode),
            folder);
        if (path is not null)
        {
            SavePath = path;
        }
    }

    [RelayCommand]
    private void Create()
    {
        _showAllErrors = true;
        var errors = InventoryValidation.ValidateCreate(BuildRequest());
        Errors.Set(errors);
        if (errors.Count == 0)
        {
            Close(true);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private void Touch(string field, bool pathsChanged)
    {
        _touched.Add(field);

        // File system checks only run when a path changes (or on Create); matter fields are checked on every edit.
        var errors = pathsChanged || _showAllErrors
            ? InventoryValidation.ValidateCreate(BuildRequest())
            : InventoryValidation.ValidateMatter(ClientName, ClientCode, MatterName, MatterCode, MatterUrl);

        if (!pathsChanged && !_showAllErrors)
        {
            // Keep any path errors already shown.
            foreach (var pathField in new[] { InventoryFields.RootFolder, InventoryFields.SavePath })
            {
                if (Errors[pathField] is { Length: > 0 } message)
                {
                    errors[pathField] = message;
                }
            }
        }

        Errors.Set(errors
            .Where(e => _showAllErrors || _touched.Contains(e.Key))
            .ToDictionary(e => e.Key, e => e.Value));
    }

    private void UpdateDefaultSavePath()
    {
        if (_savePathEditedByUser || string.IsNullOrWhiteSpace(RootFolder))
        {
            return;
        }

        _updatingSavePath = true;
        try
        {
            SavePath = Path.Combine(RootFolder, InventoryFileName.Default(ClientCode, MatterCode));
        }
        catch (ArgumentException)
        {
            // Invalid root path text; validation reports it.
        }
        finally
        {
            _updatingSavePath = false;
        }
    }

    private static string DescribeRoot(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return string.Empty;
        }

        try
        {
            var count = Directory.EnumerateDirectories(folder)
                .Count(d => !IgnoredRootFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));
            return count == 1 ? "1 subfolder found (candidate media)" : $"{count:N0} subfolders found (candidate media)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The folder's contents could not be listed.";
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

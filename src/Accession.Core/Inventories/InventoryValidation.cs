namespace Accession.Core.Inventories;

/// <summary>Field names used as keys in validation results.</summary>
public static class InventoryFields
{
    public const string ClientName = nameof(ClientName);
    public const string ClientCode = nameof(ClientCode);
    public const string MatterName = nameof(MatterName);
    public const string MatterCode = nameof(MatterCode);
    public const string Description = nameof(Description);
    public const string MatterUrl = nameof(MatterUrl);
    public const string RootFolder = nameof(RootFolder);
    public const string SavePath = nameof(SavePath);
}

/// <summary>What the user enters in the New Inventory dialog (INV-01).</summary>
public sealed record CreateInventoryRequest(
    string ClientName,
    string ClientCode,
    string MatterName,
    string MatterCode,
    string? Description,
    string? MatterUrl,
    string RootFolder,
    string SavePath);

/// <summary>Validation rules for inventory creation and matter edits (INV-01, INV-03, INV-07).</summary>
public static class InventoryValidation
{
    public const int MaxFieldLength = 200;

    /// <summary>Validates the matter fields. Returns field → message for each problem.</summary>
    public static Dictionary<string, string> ValidateMatter(
        string? clientName, string? clientCode, string? matterName, string? matterCode, string? matterUrl)
    {
        var errors = new Dictionary<string, string>();
        Required(errors, InventoryFields.ClientName, clientName, "Client Name");
        Required(errors, InventoryFields.ClientCode, clientCode, "Client ID");
        Required(errors, InventoryFields.MatterName, matterName, "Matter Name");
        Required(errors, InventoryFields.MatterCode, matterCode, "Matter ID");

        if (!string.IsNullOrWhiteSpace(matterUrl) && !IsWebUrl(matterUrl.Trim()))
        {
            errors[InventoryFields.MatterUrl] = "Enter a full web address starting with http:// or https://.";
        }

        return errors;
    }

    /// <summary>Validates a creation request, including the file system checks. Returns field → message.</summary>
    public static Dictionary<string, string> ValidateCreate(CreateInventoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = ValidateMatter(request.ClientName, request.ClientCode, request.MatterName, request.MatterCode, request.MatterUrl);

        var rootOk = false;
        if (string.IsNullOrWhiteSpace(request.RootFolder))
        {
            errors[InventoryFields.RootFolder] = "Select the root folder that contains the media folders.";
        }
        else if (!Directory.Exists(request.RootFolder))
        {
            errors[InventoryFields.RootFolder] = "The root folder does not exist or cannot be reached.";
        }
        else
        {
            rootOk = true;
        }

        var savePathError = ValidateSavePath(request.SavePath, rootOk ? request.RootFolder : null);
        if (savePathError is not null)
        {
            errors[InventoryFields.SavePath] = savePathError;
        }

        return errors;
    }

    public static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? ValidateSavePath(string? savePath, string? rootFolder)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return "Choose where to save the inventory file.";
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(savePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "The file path is not valid.";
        }

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName) || InventoryFileName.Sanitize(fileName) != fileName.Trim())
        {
            return "The file name is not valid.";
        }

        if (Directory.Exists(fullPath))
        {
            return "The path is a folder; enter a file name.";
        }

        if (File.Exists(fullPath))
        {
            return "A file with this name already exists.";
        }

        var folder = Path.GetDirectoryName(fullPath);
        if (folder is null || !Directory.Exists(folder))
        {
            return "The folder does not exist.";
        }

        if (rootFolder is not null && PathRules.IsInsideMediaFolder(fullPath, rootFolder))
        {
            return "The inventory file cannot be saved inside a media folder. Save it in the root folder or elsewhere.";
        }

        if (!CanWriteTo(folder))
        {
            return "You don't have permission to create files in this folder.";
        }

        return null;
    }

    private static void Required(Dictionary<string, string> errors, string field, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = $"{label} is required.";
        }
        else if (value.Trim().Length > MaxFieldLength)
        {
            errors[field] = $"{label} must be {MaxFieldLength} characters or fewer.";
        }
    }

    private static bool CanWriteTo(string folder)
    {
        var probe = Path.Combine(folder, $".accession-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Thrown when a request fails validation; carries field → message.</summary>
public sealed class InventoryValidationException(IReadOnlyDictionary<string, string> errors)
    : Exception("The inventory details are not valid: " + string.Join(" ", errors.Values))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

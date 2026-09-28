namespace Accession.Core.Inventories;

public static class InventoryFileName
{
    /// <summary>Inventories are SQLite databases, named ".accession" so they are not mistaken for ordinary SQLite files.</summary>
    public const string Extension = ".accession";

    /// <summary>Inventories made before version 0.2 end in ".sqlite"; they open as before.</summary>
    public const string LegacyExtension = ".sqlite";

    /// <summary>Open dialog filter: inventories of both extensions.</summary>
    public const string OpenFilter = "Accession inventory (*.accession;*.sqlite)|*.accession;*.sqlite|All files (*.*)|*.*";

    /// <summary>Save dialog filter for new inventories.</summary>
    public const string SaveFilter = "Accession inventory (*.accession)|*.accession";

    // Characters Windows does not allow in file names (Path.GetInvalidFileNameChars differs by OS).
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Default name <c>{ClientID}_{MatterID}_Inventory.accession</c> with invalid characters replaced by <c>_</c> (INV-02).</summary>
    public static string Default(string? clientCode, string? matterCode)
    {
        var parts = new[] { Sanitize(clientCode), Sanitize(matterCode) }.Where(p => p.Length > 0).Append("Inventory");
        return string.Join('_', parts) + Extension;
    }

    /// <summary>A path typed without an extension gets ".accession".</summary>
    public static string WithExtension(string path) =>
        string.IsNullOrWhiteSpace(path) || Path.HasExtension(path.Trim()) ? path : path.Trim() + Extension;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().Select(c => char.IsControl(c) || InvalidChars.Contains(c) ? '_' : c).ToArray();
        return new string(chars).TrimEnd('.', ' ');
    }
}

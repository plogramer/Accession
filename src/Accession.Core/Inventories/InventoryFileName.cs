namespace Accession.Core.Inventories;

public static class InventoryFileName
{
    public const string Extension = ".sqlite";

    // Characters Windows does not allow in file names (Path.GetInvalidFileNameChars differs by OS).
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Default name <c>{ClientID}_{MatterID}_Inventory.sqlite</c> with invalid characters replaced by <c>_</c> (INV-02).</summary>
    public static string Default(string? clientCode, string? matterCode)
    {
        var parts = new[] { Sanitize(clientCode), Sanitize(matterCode) }.Where(p => p.Length > 0).Append("Inventory");
        return string.Join('_', parts) + Extension;
    }

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

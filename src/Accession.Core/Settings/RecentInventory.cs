namespace Accession.Core.Settings;

/// <summary>An entry in the Start window's recent inventories list.</summary>
public sealed class RecentInventory
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Client / matter text shown in the list, e.g. "ACME Corporation – Smith v. ACME".</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset LastOpenedUtc { get; set; }
}

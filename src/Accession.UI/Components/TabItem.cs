namespace Accession.UI.Components;

/// <summary>One tab of a <see cref="Tabs"/> strip.</summary>
public sealed record TabItem(string Key, string Title, string? Badge = null);

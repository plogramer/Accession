namespace Accession.Core.Settings;

/// <summary>Saved layout of one grid column.</summary>
public sealed class GridColumnLayout
{
    /// <summary>Stable column identifier (not the localized header).</summary>
    public string Key { get; set; } = string.Empty;

    public double Width { get; set; }
    public int DisplayIndex { get; set; }
    public bool IsVisible { get; set; } = true;
}

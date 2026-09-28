namespace Accession.UI.Components;

/// <summary>A right-click on a table row: the row and the pointer position (0, 0 when opened from the keyboard).</summary>
public sealed record RowContextMenuArgs<TItem>(TItem Item, double X, double Y);

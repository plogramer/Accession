using Accession.Core.Settings;
using Accession.Data.Browsing;

namespace Accession.Data.Export;

/// <summary>The sheets an export can contain (EXP-02).</summary>
public enum ExportSheet
{
    Summary,
    Media,
    Categories,
    Extensions,
    Files,
    Errors,
}

/// <summary>What to export and where (EXP-01, EXP-02, EXP-04).</summary>
public sealed record ExportRequest
{
    public static readonly IReadOnlySet<ExportSheet> AllSheets = new HashSet<ExportSheet>(Enum.GetValues<ExportSheet>());

    /// <summary>The workbook to write; with <see cref="OneWorkbookPerMedia"/>, the Media ID is added to its name.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Media to export; null = all media.</summary>
    public IReadOnlyCollection<long>? MediaKeys { get; init; }

    /// <summary>
    /// The current File browser view: the Files sheet holds exactly these files, and the other sheets cover the media
    /// the view is limited to (all media if none).
    /// </summary>
    public FileFilter? FilesView { get; init; }

    public IReadOnlySet<ExportSheet> Sheets { get; init; } = AllSheets;

    /// <summary>Unit system for the readable size columns (from Settings).</summary>
    public SizeUnitSystem SizeUnit { get; init; } = SizeUnitSystem.Decimal;

    /// <summary>One workbook per media instead of one for all (EXP-04 option).</summary>
    public bool OneWorkbookPerMedia { get; init; }

    /// <summary>Data rows per sheet before splitting; lowered by tests only.</summary>
    internal int MaxRowsPerSheet { get; init; } = Core.Export.XlsxWriter.ExcelMaxDataRows;
}

/// <summary>Row counts known before exporting, for the dialog's "Estimated rows" line.</summary>
public sealed record ExportEstimate(long FileRows, long ErrorRows, long OtherRows, int FilesSheets, int Workbooks)
{
    public long TotalRows => FileRows + ErrorRows + OtherRows;
}

/// <summary>Progress: rows written so far of the estimated total.</summary>
public sealed record ExportProgress(long RowsWritten, long TotalRows, string Stage);

/// <summary>A written workbook and the rows in each of its sheets.</summary>
public sealed record ExportedWorkbook(string Path, IReadOnlyList<(string Name, long Rows)> Sheets)
{
    public long TotalRows => Sheets.Sum(s => s.Rows);
}

public sealed record ExportResult(IReadOnlyList<ExportedWorkbook> Workbooks)
{
    public long TotalRows => Workbooks.Sum(w => w.TotalRows);
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Accession.Core.Export;

/// <summary>How a column's cells are written.</summary>
public enum XlsxCellType
{
    Text,

    /// <summary>Whole number, shown with thousands separators.</summary>
    Integer,

    /// <summary>Decimal number, shown with two decimals.</summary>
    Number,

    /// <summary>A UTC point in time, shown as yyyy-mm-dd hh:mm:ss (the header should say "UTC").</summary>
    DateTimeUtc,
}

/// <summary>A column: header text, cell type and width in characters (0 = Excel's default).</summary>
public sealed record XlsxColumn(string Header, XlsxCellType Type = XlsxCellType.Text, double Width = 0);

/// <summary>
/// Streams an <c>.xlsx</c> workbook to disk with constant memory (requirements EXP-04, EXP-05): each row goes straight
/// into the zip entry of its sheet, strings are written inline (no shared-string table), and the workbook part, which
/// lists the sheets, is written last. A sheet that reaches <see cref="MaxDataRows"/> continues on a new sheet:
/// <c>Files (1)</c>, <c>Files (2)</c>, … Every sheet has a bold, frozen header row with an auto-filter.
/// <para>
/// The file is written to <c>{path}.partial</c> and moved to <c>path</c> by <see cref="Complete"/>; disposing
/// without completing (cancel, error) deletes the partial file, so no half-written workbook is left behind.
/// </para>
/// </summary>
/// <remarks>
/// Written directly with <see cref="ZipArchive"/> and <see cref="XmlWriter"/> instead of the Open XML SDK: the SDK
/// writes through System.IO.Packaging, which can keep whole parts in memory, and the SpreadsheetML needed here is small.
/// </remarks>
public sealed class XlsxWriter : IDisposable
{
    /// <summary>Excel's limit is 1,048,576 rows per sheet, including the header row.</summary>
    public const int ExcelMaxDataRows = 1_048_575;

    /// <summary>Longest text Excel keeps in a cell.</summary>
    public const int MaxCellText = 32_767;

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const int ProgressEvery = 5_000;

    // Style indexes in styles.xml (cellXfs).
    private const int StyleHeader = 1;
    private const int StyleDate = 2;
    private const int StyleInteger = 3;
    private const int StyleNumber = 4;

    private static readonly XmlWriterSettings XmlSettings = new() { Encoding = new UTF8Encoding(false), CloseOutput = true };
    private static readonly DateTime OaEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _path;
    private readonly string _partialPath;
    private readonly ZipArchive _zip;
    private readonly List<SheetInfo> _sheets = [];
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<long>? _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly StringBuilder _cellRef = new(12);

    private XmlWriter? _sheet;
    private SheetInfo? _current;
    private IReadOnlyList<XlsxColumn> _columns = [];
    private string _baseName = string.Empty;
    private int _part;
    private int _column = -1;
    private bool _completed;

    private XlsxWriter(string path, int maxDataRows, Action<long>? progress, CancellationToken cancellationToken)
    {
        _path = Path.GetFullPath(path);
        _partialPath = _path + ".partial";
        MaxDataRows = maxDataRows;
        _progress = progress;
        _cancellationToken = cancellationToken;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _zip = new ZipArchive(new FileStream(_partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16), ZipArchiveMode.Create);
    }

    /// <summary>Data rows per sheet before continuing on a new sheet (Excel's limit unless a test lowers it).</summary>
    public int MaxDataRows { get; }

    /// <summary>Data rows written so far, across all sheets.</summary>
    public long RowsWritten { get; private set; }

    /// <summary>The sheets written so far, with their final names and data row counts.</summary>
    public IReadOnlyList<(string Name, long Rows)> Sheets => [.. _sheets.Select(s => (s.Name, s.Rows))];

    /// <summary>Starts a workbook at <paramref name="path"/>.</summary>
    /// <param name="progress">Called with <see cref="RowsWritten"/> every few thousand rows.</param>
    /// <param name="cancellationToken">Checked while writing rows; cancelling throws <see cref="OperationCanceledException"/>.</param>
    public static XlsxWriter Create(string path, Action<long>? progress = null, CancellationToken cancellationToken = default,
        int maxDataRows = ExcelMaxDataRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDataRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDataRows, ExcelMaxDataRows);
        return new XlsxWriter(path, maxDataRows, progress, cancellationToken);
    }

    /// <summary>
    /// Starts a sheet named <paramref name="name"/> (made valid and unique) with a header row. If it outgrows
    /// <see cref="MaxDataRows"/>, it continues on <c>name (2)</c>, … and the first part is renamed <c>name (1)</c>.
    /// </summary>
    public void BeginSheet(string name, IReadOnlyList<XlsxColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfZero(columns.Count);
        EnsureOpen();
        EndSheet();
        _baseName = SanitizeSheetName(name);
        _columns = columns;
        _part = 1;
        OpenSheetPart(UniqueName(_baseName));
    }

    /// <summary>Starts a data row; follow with one call per column (<see cref="Text"/>, <see cref="Integer"/>, …) and <see cref="EndRow"/>.</summary>
    public void BeginRow()
    {
        if (_current is null || _sheet is null)
        {
            throw new InvalidOperationException("Call BeginSheet first.");
        }

        if (_current.Rows == MaxDataRows)
        {
            ContinueOnNewSheet();
        }

        if (RowsWritten % 1_000 == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }

        _current.Rows++;
        _sheet!.WriteStartElement("row", MainNs);
        _sheet.WriteAttributeString("r", RowNumber());
        _column = 0;
    }

    public void Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Skip();
            return;
        }

        StartCell(style: 0, type: "inlineStr");
        _sheet!.WriteStartElement("is", MainNs);
        _sheet.WriteStartElement("t", MainNs);
        var text = CleanText(value);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            _sheet.WriteAttributeString("xml", "space", null, "preserve");
        }

        _sheet.WriteString(text);
        _sheet.WriteEndElement();
        _sheet.WriteEndElement();
        EndCell();
    }

    public void Integer(long? value)
    {
        if (value is not { } number)
        {
            Skip();
            return;
        }

        StartCell(StyleInteger);
        WriteValue(number.ToString(CultureInfo.InvariantCulture));
    }

    public void Number(double? value)
    {
        if (value is not { } number || double.IsNaN(number) || double.IsInfinity(number))
        {
            Skip();
            return;
        }

        StartCell(StyleNumber);
        WriteValue(number.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>A UTC time (Excel has no time zones: the value is written as UTC).</summary>
    public void DateTimeUtc(DateTimeOffset? value)
    {
        if (value is not { } time)
        {
            Skip();
            return;
        }

        StartCell(StyleDate);
        WriteValue((time.UtcDateTime - OaEpoch).TotalDays.ToString("R", CultureInfo.InvariantCulture));
    }

    public void EndRow()
    {
        if (_column < 0)
        {
            throw new InvalidOperationException("Call BeginRow first.");
        }

        _sheet!.WriteEndElement();
        _column = -1;
        RowsWritten++;
        if (RowsWritten % ProgressEvery == 0)
        {
            _progress?.Invoke(RowsWritten);
        }
    }

    /// <summary>Writes a row from values matching the columns' types (strings, numbers, <see cref="DateTimeOffset"/>, null).</summary>
    public void WriteRow(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        BeginRow();
        for (var i = 0; i < _columns.Count; i++)
        {
            var value = i < values.Length ? values[i] : null;
            switch (_columns[i].Type)
            {
                case XlsxCellType.Integer:
                    Integer(value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;
                case XlsxCellType.Number:
                    Number(value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    break;
                case XlsxCellType.DateTimeUtc:
                    DateTimeUtc(value switch
                    {
                        null => null,
                        DateTimeOffset o => o,
                        DateTime d => new DateTimeOffset(d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime()),
                        _ => throw new ArgumentException($"Column {i + 1} expects a date, not {value.GetType().Name}.", nameof(values)),
                    });
                    break;
                default:
                    Text(value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        EndRow();
    }

    /// <summary>Finishes the workbook and moves it to its final path (replacing an existing file).</summary>
    public void Complete()
    {
        EnsureOpen();
        EndSheet();
        if (_sheets.Count == 0)
        {
            throw new InvalidOperationException("A workbook needs at least one sheet.");
        }

        WriteWorkbook();
        WriteStyles();
        WriteContentTypes();
        WritePart("_rels/.rels", w =>
        {
            w.WriteStartElement("Relationships", PackageRelNs);
            Relationship(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
            w.WriteEndElement();
        });
        _zip.Dispose();
        File.Move(_partialPath, _path, overwrite: true);
        _completed = true;
    }

    /// <summary>Deletes the partial file unless <see cref="Complete"/> succeeded.</summary>
    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        try
        {
            _sheet?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or XmlException)
        {
            // Closing a half-written sheet can fail; the file is deleted below anyway.
        }

        try
        {
            _zip.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
        }

        File.Delete(_partialPath);
    }

    /// <summary>A valid sheet name: no <c>[ ] : * ? / \</c>, no leading or trailing apostrophe, at most 31 characters.</summary>
    public static string SanitizeSheetName(string? name)
    {
        var chars = (name ?? string.Empty).Select(c => c is '[' or ']' or ':' or '*' or '?' or '/' or '\\' || char.IsControl(c) ? '_' : c);
        var cleaned = new string([.. chars]).Trim().Trim('\'').Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }

        return cleaned.Length > 31 ? cleaned[..31].TrimEnd() : cleaned;
    }

    private void OpenSheetPart(string name)
    {
        var index = _sheets.Count + 1;
        _current = new SheetInfo(name, $"xl/worksheets/sheet{index}.xml", _columns.Count);
        _sheets.Add(_current);
        _sheet = XmlWriter.Create(_zip.CreateEntry(_current.PartName, CompressionLevel.Fastest).Open(), XmlSettings);
        _sheet.WriteStartDocument(true);
        _sheet.WriteStartElement("worksheet", MainNs);
        _sheet.WriteAttributeString("xmlns", "r", null, RelNs);

        _sheet.WriteStartElement("sheetViews", MainNs);
        _sheet.WriteStartElement("sheetView", MainNs);
        if (index == 1)
        {
            _sheet.WriteAttributeString("tabSelected", "1");
        }

        _sheet.WriteAttributeString("workbookViewId", "0");
        _sheet.WriteStartElement("pane", MainNs);
        _sheet.WriteAttributeString("ySplit", "1");
        _sheet.WriteAttributeString("topLeftCell", "A2");
        _sheet.WriteAttributeString("activePane", "bottomLeft");
        _sheet.WriteAttributeString("state", "frozen");
        _sheet.WriteEndElement();
        _sheet.WriteEndElement();
        _sheet.WriteEndElement();

        if (_columns.Any(c => c.Width > 0))
        {
            _sheet.WriteStartElement("cols", MainNs);
            for (var i = 0; i < _columns.Count; i++)
            {
                if (_columns[i].Width > 0)
                {
                    var col = (i + 1).ToString(CultureInfo.InvariantCulture);
                    _sheet.WriteStartElement("col", MainNs);
                    _sheet.WriteAttributeString("min", col);
                    _sheet.WriteAttributeString("max", col);
                    _sheet.WriteAttributeString("width", _columns[i].Width.ToString(CultureInfo.InvariantCulture));
                    _sheet.WriteAttributeString("customWidth", "1");
                    _sheet.WriteEndElement();
                }
            }

            _sheet.WriteEndElement();
        }

        _sheet.WriteStartElement("sheetData", MainNs);
        _sheet.WriteStartElement("row", MainNs);
        _sheet.WriteAttributeString("r", "1");
        _column = 0;
        foreach (var column in _columns)
        {
            StartCell(StyleHeader, "inlineStr", rowNumber: 1);
            _sheet.WriteStartElement("is", MainNs);
            _sheet.WriteElementString("t", MainNs, CleanText(column.Header));
            _sheet.WriteEndElement();
            EndCell();
        }

        _sheet.WriteEndElement();
        _column = -1;
    }

    private void ContinueOnNewSheet()
    {
        EndSheet();
        if (_part == 1)
        {
            // The first part gets its number now that there is a second one.
            var first = _sheets[^1];
            _usedNames.Remove(first.Name);
            first.Name = UniqueName(PartName(_baseName, 1));
        }

        _part++;
        OpenSheetPart(UniqueName(PartName(_baseName, _part)));
    }

    private void EndSheet()
    {
        if (_sheet is null || _current is null)
        {
            return;
        }

        if (_column >= 0)
        {
            throw new InvalidOperationException("The last row was not ended.");
        }

        _sheet.WriteEndElement(); // sheetData
        _current.FilterRef = $"A1:{ColumnName(_columns.Count - 1)}{_current.Rows + 1}";
        _sheet.WriteStartElement("autoFilter", MainNs);
        _sheet.WriteAttributeString("ref", _current.FilterRef);
        _sheet.WriteEndElement();
        _sheet.WriteEndElement(); // worksheet
        _sheet.WriteEndDocument();
        _sheet.Dispose();
        _sheet = null;
        _current = null;
    }

    private void WriteWorkbook()
    {
        WritePart("xl/workbook.xml", w =>
        {
            w.WriteStartElement("workbook", MainNs);
            w.WriteAttributeString("xmlns", "r", null, RelNs);
            w.WriteStartElement("bookViews", MainNs);
            w.WriteStartElement("workbookView", MainNs);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("sheets", MainNs);
            for (var i = 0; i < _sheets.Count; i++)
            {
                w.WriteStartElement("sheet", MainNs);
                w.WriteAttributeString("name", _sheets[i].Name);
                w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("r", "id", RelNs, $"rId{i + 1}");
                w.WriteEndElement();
            }

            w.WriteEndElement();

            // Excel records each sheet's auto-filter range as a hidden name.
            w.WriteStartElement("definedNames", MainNs);
            for (var i = 0; i < _sheets.Count; i++)
            {
                var range = _sheets[i].FilterRef.Split(':');
                w.WriteStartElement("definedName", MainNs);
                w.WriteAttributeString("name", "_xlnm._FilterDatabase");
                w.WriteAttributeString("localSheetId", i.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("hidden", "1");
                w.WriteString($"'{_sheets[i].Name.Replace("'", "''", StringComparison.Ordinal)}'!{Absolute(range[0])}:{Absolute(range[1])}");
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        });

        WritePart("xl/_rels/workbook.xml.rels", w =>
        {
            w.WriteStartElement("Relationships", PackageRelNs);
            for (var i = 0; i < _sheets.Count; i++)
            {
                Relationship(w, $"rId{i + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    $"worksheets/sheet{i + 1}.xml");
            }

            Relationship(w, $"rId{_sheets.Count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
            w.WriteEndElement();
        });
    }

    private void WriteStyles()
    {
        WritePart("xl/styles.xml", w =>
        {
            w.WriteStartElement("styleSheet", MainNs);
            w.WriteStartElement("numFmts", MainNs);
            w.WriteAttributeString("count", "1");
            w.WriteStartElement("numFmt", MainNs);
            w.WriteAttributeString("numFmtId", "164");
            w.WriteAttributeString("formatCode", "yyyy-mm-dd hh:mm:ss");
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("fonts", MainNs);
            w.WriteAttributeString("count", "2");
            Font(w, bold: false);
            Font(w, bold: true);
            w.WriteEndElement();

            w.WriteStartElement("fills", MainNs);
            w.WriteAttributeString("count", "2");
            foreach (var pattern in new[] { "none", "gray125" })
            {
                w.WriteStartElement("fill", MainNs);
                w.WriteStartElement("patternFill", MainNs);
                w.WriteAttributeString("patternType", pattern);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement();

            w.WriteStartElement("borders", MainNs);
            w.WriteAttributeString("count", "1");
            w.WriteStartElement("border", MainNs);
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" })
            {
                w.WriteElementString(side, MainNs, string.Empty);
            }

            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("cellStyleXfs", MainNs);
            w.WriteAttributeString("count", "1");
            Xf(w, numFmt: 0, font: 0, inCellXfs: false);
            w.WriteEndElement();

            // Order matches the Style* constants.
            w.WriteStartElement("cellXfs", MainNs);
            w.WriteAttributeString("count", "5");
            Xf(w, numFmt: 0, font: 0);
            Xf(w, numFmt: 0, font: 1);
            Xf(w, numFmt: 164, font: 0);
            Xf(w, numFmt: 3, font: 0);
            Xf(w, numFmt: 4, font: 0);
            w.WriteEndElement();

            w.WriteStartElement("cellStyles", MainNs);
            w.WriteAttributeString("count", "1");
            w.WriteStartElement("cellStyle", MainNs);
            w.WriteAttributeString("name", "Normal");
            w.WriteAttributeString("xfId", "0");
            w.WriteAttributeString("builtinId", "0");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        });

        static void Font(XmlWriter w, bool bold)
        {
            w.WriteStartElement("font", MainNs);
            if (bold)
            {
                w.WriteElementString("b", MainNs, string.Empty);
            }

            w.WriteStartElement("sz", MainNs);
            w.WriteAttributeString("val", "11");
            w.WriteEndElement();
            w.WriteStartElement("name", MainNs);
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteStartElement("family", MainNs);
            w.WriteAttributeString("val", "2");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void Xf(XmlWriter w, int numFmt, int font, bool inCellXfs = true)
        {
            w.WriteStartElement("xf", MainNs);
            w.WriteAttributeString("numFmtId", numFmt.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", "0");
            w.WriteAttributeString("borderId", "0");
            if (inCellXfs)
            {
                w.WriteAttributeString("xfId", "0");
                if (numFmt != 0)
                {
                    w.WriteAttributeString("applyNumberFormat", "1");
                }

                if (font != 0)
                {
                    w.WriteAttributeString("applyFont", "1");
                }
            }

            w.WriteEndElement();
        }
    }

    private void WriteContentTypes()
    {
        const string ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        WritePart("[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", ctNs);
            Default("rels", "application/vnd.openxmlformats-package.relationships+xml");
            Default("xml", "application/xml");
            Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            Override("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            foreach (var sheet in _sheets)
            {
                Override("/" + sheet.PartName, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            }

            w.WriteEndElement();

            void Default(string extension, string type)
            {
                w.WriteStartElement("Default", ctNs);
                w.WriteAttributeString("Extension", extension);
                w.WriteAttributeString("ContentType", type);
                w.WriteEndElement();
            }

            void Override(string part, string type)
            {
                w.WriteStartElement("Override", ctNs);
                w.WriteAttributeString("PartName", part);
                w.WriteAttributeString("ContentType", type);
                w.WriteEndElement();
            }
        });
    }

    private void WritePart(string name, Action<XmlWriter> write)
    {
        using var w = XmlWriter.Create(_zip.CreateEntry(name, CompressionLevel.Fastest).Open(), XmlSettings);
        w.WriteStartDocument(true);
        write(w);
        w.WriteEndDocument();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PackageRelNs);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private void StartCell(int style, string? type = null, long? rowNumber = null)
    {
        if (_column < 0)
        {
            throw new InvalidOperationException("Call BeginRow first.");
        }

        if (_column >= _columns.Count)
        {
            throw new InvalidOperationException($"The sheet has {_columns.Count} columns.");
        }

        _sheet!.WriteStartElement("c", MainNs);
        _cellRef.Clear().Append(ColumnName(_column)).Append(rowNumber ?? _current!.Rows + 1);
        _sheet.WriteAttributeString("r", _cellRef.ToString());
        if (style != 0)
        {
            _sheet.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        }

        if (type is not null)
        {
            _sheet.WriteAttributeString("t", type);
        }
    }

    private void WriteValue(string value)
    {
        _sheet!.WriteElementString("v", MainNs, value);
        EndCell();
    }

    private void EndCell()
    {
        _sheet!.WriteEndElement();
        _column++;
    }

    /// <summary>An empty cell: nothing is written, the next value moves one column on.</summary>
    private void Skip()
    {
        if (_column < 0)
        {
            throw new InvalidOperationException("Call BeginRow first.");
        }

        _column++;
    }

    private string RowNumber() => (_current!.Rows + 1).ToString(CultureInfo.InvariantCulture);

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var n = 2; !_usedNames.Add(candidate); n++)
        {
            candidate = PartName(name, n);
        }

        return candidate;
    }

    /// <summary>"Files" + 2 → "Files (2)", shortened so the whole name stays within 31 characters.</summary>
    private static string PartName(string baseName, int part)
    {
        var suffix = $" ({part.ToString(CultureInfo.InvariantCulture)})";
        return (baseName.Length + suffix.Length > 31 ? baseName[..(31 - suffix.Length)].TrimEnd() : baseName) + suffix;
    }

    /// <summary>Text Excel and XML accept: characters XML 1.0 forbids become U+FFFD; longer text is cut at <see cref="MaxCellText"/>.</summary>
    private static string CleanText(string text)
    {
        if (text.Length > MaxCellText)
        {
            text = text[..MaxCellText];
        }

        var clean = true;
        for (var i = 0; i < text.Length && clean; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (!XmlConvert.IsXmlChar(c))
            {
                clean = false;
            }
        }

        if (clean)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[++i]);
            }
            else
            {
                builder.Append(XmlConvert.IsXmlChar(c) ? c : '�');
            }
        }

        return builder.ToString();
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>"A1" → "$A$1".</summary>
    private static string Absolute(string cell)
    {
        var digits = cell.IndexOfAny("0123456789".ToCharArray());
        return $"${cell[..digits]}${cell[digits..]}";
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_completed, this);

    private sealed class SheetInfo(string name, string partName, int columns)
    {
        public string Name { get; set; } = name;

        public string PartName { get; } = partName;

        public int Columns { get; } = columns;

        public long Rows { get; set; }

        public string FilterRef { get; set; } = "A1:A1";
    }
}

using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Accession.Core.Export;
using Accession.Tests.TestSupport;

namespace Accession.Tests.Export;

/// <summary>The streaming .xlsx writer (EXP-04, EXP-05): cell types, sheet splitting, names, cancel and memory.</summary>
public sealed class XlsxWriterTests : IDisposable
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string PathFor(string name) => Path.Combine(_dir.Path, name);

    [Fact]
    public void Writes_typed_cells_with_a_bold_frozen_filtered_header()
    {
        var path = PathFor("types.xlsx");
        using (var writer = XlsxWriter.Create(path))
        {
            writer.BeginSheet("Files", [
                new XlsxColumn("Name"), new XlsxColumn("Size", XlsxCellType.Integer), new XlsxColumn("Ratio", XlsxCellType.Number),
                new XlsxColumn("Modified (UTC)", XlsxCellType.DateTimeUtc, 20)]);
            writer.WriteRow(" report.docx", 1_234_567L, 0.5, new DateTimeOffset(2024, 3, 1, 12, 30, 0, TimeSpan.FromHours(2)));
            writer.WriteRow(null, null, null, null);
            writer.Complete();
        }

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["Files"], SheetNames(zip));
        var sheet = Part(zip, "xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(Main + "row").ToList();
        Assert.Equal(3, rows.Count);

        var header = rows[0].Elements(Main + "c").ToList();
        Assert.All(header, c => Assert.Equal("1", (string?)c.Attribute("s")));
        Assert.Equal("Modified (UTC)", header[3].Value);

        var cells = rows[1].Elements(Main + "c").ToList();
        Assert.Equal("inlineStr", (string?)cells[0].Attribute("t"));
        Assert.Equal(" report.docx", cells[0].Value); // leading space kept
        Assert.Equal("1234567", cells[1].Element(Main + "v")!.Value);
        Assert.Equal("0.5", cells[2].Element(Main + "v")!.Value);
        Assert.Equal("D2", (string?)cells[3].Attribute("r"));
        var days = double.Parse(cells[3].Element(Main + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(new DateTime(2024, 3, 1, 10, 30, 0), DateTime.FromOADate(days), TimeSpan.FromSeconds(1)); // stored as UTC

        Assert.Empty(rows[2].Elements(Main + "c")); // nulls are empty cells
        Assert.Equal("frozen", (string?)sheet.Descendants(Main + "pane").Single().Attribute("state"));
        Assert.Equal("A1:D3", (string?)sheet.Descendants(Main + "autoFilter").Single().Attribute("ref"));
        Assert.Equal("20", (string?)sheet.Descendants(Main + "col").Single().Attribute("width"));
    }

    [Fact]
    public void A_full_sheet_continues_on_numbered_sheets()
    {
        var path = PathFor("split.xlsx");
        using (var writer = XlsxWriter.Create(path, maxDataRows: 1_000))
        {
            writer.BeginSheet("Summary", [new XlsxColumn("Item")]);
            writer.WriteRow("Total");
            writer.BeginSheet("Files", [new XlsxColumn("N", XlsxCellType.Integer)]);
            for (var i = 0; i < 2_500; i++)
            {
                writer.WriteRow(i);
            }

            writer.BeginSheet("Errors", [new XlsxColumn("Message")]);
            writer.Complete();

            Assert.Equal([("Summary", 1L), ("Files (1)", 1_000L), ("Files (2)", 1_000L), ("Files (3)", 500L), ("Errors", 0L)], writer.Sheets);
            Assert.Equal(2_501, writer.RowsWritten);
        }

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["Summary", "Files (1)", "Files (2)", "Files (3)", "Errors"], SheetNames(zip));
        Assert.Equal(1_001, CountRows(zip, "xl/worksheets/sheet2.xml")); // header + 1,000
        Assert.Equal(501, CountRows(zip, "xl/worksheets/sheet4.xml"));
        var third = Part(zip, "xl/worksheets/sheet4.xml");
        Assert.Equal("N", third.Descendants(Main + "row").First().Value); // every part repeats the header
        Assert.Equal("2000", third.Descendants(Main + "row").ElementAt(1).Value);
        Assert.Equal("A1:A501", (string?)third.Descendants(Main + "autoFilter").Single().Attribute("ref"));
    }

    [Fact]
    public void Sheet_names_are_made_valid_and_unique()
    {
        Assert.Equal("Media_ 1_2", XlsxWriter.SanitizeSheetName("'Media: 1/2'"));
        Assert.Equal("Sheet", XlsxWriter.SanitizeSheetName("  "));
        Assert.Equal(31, XlsxWriter.SanitizeSheetName(new string('x', 40)).Length);

        var path = PathFor("names.xlsx");
        using (var writer = XlsxWriter.Create(path, maxDataRows: 1))
        {
            writer.BeginSheet("Files", [new XlsxColumn("A")]);
            writer.BeginSheet("files", [new XlsxColumn("A")]);
            writer.BeginSheet(new string('m', 40), [new XlsxColumn("A")]);
            writer.WriteRow("1");
            writer.WriteRow("2");
            writer.Complete();
        }

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["Files", "files (2)", new string('m', 27) + " (1)", new string('m', 27) + " (2)"], SheetNames(zip));
    }

    [Fact]
    public void Text_is_cleaned_of_characters_xml_cannot_hold_and_cut_at_excels_limit()
    {
        var path = PathFor("text.xlsx");
        using (var writer = XlsxWriter.Create(path))
        {
            writer.BeginSheet("T", [new XlsxColumn("A")]);
            writer.WriteRow("bad\u0001name\uD800.txt");
            writer.WriteRow(new string('a', 40_000));
            writer.WriteRow("emoji 😀 ok");
            writer.Complete();
        }

        using var zip = ZipFile.OpenRead(path);
        var values = Part(zip, "xl/worksheets/sheet1.xml").Descendants(Main + "t").Select(t => t.Value).Skip(1).ToList();
        Assert.Equal("bad�name�.txt", values[0]);
        Assert.Equal(XlsxWriter.MaxCellText, values[1].Length);
        Assert.Equal("emoji 😀 ok", values[2]);
    }

    [Fact]
    public void Cancel_leaves_no_file_behind()
    {
        var path = PathFor("cancelled.xlsx");
        using var cancel = new CancellationTokenSource();
        var writer = XlsxWriter.Create(path, cancellationToken: cancel.Token);
        writer.BeginSheet("Files", [new XlsxColumn("N", XlsxCellType.Integer)]);

        Assert.Throws<OperationCanceledException>(() =>
        {
            for (var i = 0; i < 100_000; i++)
            {
                if (i == 20_000)
                {
                    cancel.Cancel();
                }

                writer.WriteRow(i);
            }
        });
        writer.Dispose();

        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Completing_replaces_an_existing_file_and_reports_progress()
    {
        var path = PathFor("replace.xlsx");
        File.WriteAllText(path, "old");
        var reported = new List<long>();
        using (var writer = XlsxWriter.Create(path, reported.Add))
        {
            writer.BeginSheet("N", [new XlsxColumn("N", XlsxCellType.Integer)]);
            for (var i = 0; i < 12_000; i++)
            {
                writer.WriteRow(i);
            }

            writer.Complete();
        }

        Assert.Equal([5_000L, 10_000L], reported);
        Assert.Equal(["N"], SheetNames(ZipFile.OpenRead(path)));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public void Two_and_a_half_million_rows_make_three_sheets_with_flat_memory()
    {
        var path = PathFor("big.xlsx");
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        long peak = 0;
        using (var writer = XlsxWriter.Create(path, rows =>
               {
                   if (rows % 500_000 == 0)
                   {
                       peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true)); // live memory, not garbage
                   }
               }))
        {
            writer.BeginSheet("Files", [new XlsxColumn("Path"), new XlsxColumn("Size", XlsxCellType.Integer)]);
            for (var i = 0; i < 2_500_000; i++)
            {
                writer.BeginRow();
                writer.Text("folder\\file.txt");
                writer.Integer(i);
                writer.EndRow();
            }

            writer.Complete();
            Assert.Equal(["Files (1)", "Files (2)", "Files (3)"], writer.Sheets.Select(s => s.Name));
            Assert.Equal([1_048_575L, 1_048_575L, 402_850L], writer.Sheets.Select(s => s.Rows));
        }

        // Other tests run in parallel, so allow some noise; a writer that kept rows would grow by hundreds of MB.
        Assert.True(peak - baseline < 50_000_000, $"Live managed memory grew by {(peak - baseline) / 1_000_000} MB while writing.");
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(402_851, CountRows(zip, "xl/worksheets/sheet3.xml"));
    }

    private static List<string> SheetNames(ZipArchive zip) =>
        [.. Part(zip, "xl/workbook.xml").Descendants(Main + "sheet").Select(s => (string)s.Attribute("name")!)];

    private static XDocument Part(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private static int CountRows(ZipArchive zip, string name)
    {
        using var reader = XmlReader.Create(zip.GetEntry(name)!.Open());
        var rows = 0;
        while (reader.ReadToFollowing("row", Main.NamespaceName))
        {
            rows++;
        }

        return rows;
    }
}

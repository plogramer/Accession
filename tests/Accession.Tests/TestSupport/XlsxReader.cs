using System.IO.Compression;
using System.Xml.Linq;

namespace Accession.Tests.TestSupport;

/// <summary>Reads the sheets of an .xlsx written by XlsxWriter back as text (inline strings and raw values).</summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Sheet name → rows (header first) → cells ("" for empty cells).</summary>
    public static List<(string Name, List<List<string>> Rows)> Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var rels = Load(zip, "xl/_rels/workbook.xml.rels").Descendants(PackageRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => "xl/" + (string)r.Attribute("Target")!);
        return
        [
            .. Load(zip, "xl/workbook.xml").Descendants(Main + "sheet").Select(s =>
                ((string)s.Attribute("name")!, ReadSheet(Load(zip, rels[(string)s.Attribute(Rel + "id")!])))),
        ];
    }

    /// <summary>The rows of the named sheet, without the header.</summary>
    public static List<List<string>> Data(this List<(string Name, List<List<string>> Rows)> sheets, string name) =>
        [.. sheets.Single(s => s.Name == name).Rows.Skip(1)];

    private static List<List<string>> ReadSheet(XDocument sheet) =>
    [
        .. sheet.Descendants(Main + "row").Select(row =>
        {
            var cells = new List<string>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var column = ColumnIndex((string)c.Attribute("r")!);
                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                cells.Add((string?)c.Attribute("t") == "inlineStr" ? c.Element(Main + "is")!.Value : c.Element(Main + "v")?.Value ?? string.Empty);
            }

            return cells;
        }),
    ];

    private static int ColumnIndex(string cellRef)
    {
        var index = 0;
        foreach (var ch in cellRef.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (ch - 'A' + 1);
        }

        return index - 1;
    }

    private static XDocument Load(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }
}

namespace Accession.Data.Schema;

/// <summary>A file category as seeded into <c>FileCategory</c>.</summary>
/// <param name="Extensions">Named extensions. Each may appear in only one category.</param>
/// <param name="SplitPartExtensions">Generated split-part extensions (e01, 001…). Lower priority: a named extension
/// in any category wins (e.g. "123" stays a Lotus 1-2-3 spreadsheet).</param>
public sealed record FileCategoryDefinition(
    int CategoryId,
    string Name,
    string Description,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string>? SplitPartExtensions = null);

/// <summary>
/// Application-defined file categories and extension mappings (requirements 5.6 and Appendix A).
/// Users cannot edit these in the UI; a new app version changes them through a schema migration.
/// </summary>
public static class CategoryCatalog
{
    /// <summary>Category for files without an extension. Assigned at query time; has no mapping rows.</summary>
    public const int NoExtensionId = 19;

    /// <summary>Category for any extension not in the mapping. Assigned at query time; has no mapping rows.</summary>
    public const int OtherUnknownId = 20;

    public static IReadOnlyList<FileCategoryDefinition> Categories { get; } = Build();

    /// <summary>Extension → category ID for every mapped extension (lowercase, no dot).</summary>
    public static IReadOnlyDictionary<string, int> ExtensionMap { get; } = BuildMap(Categories);

    private static List<FileCategoryDefinition> Build()
    {
        string[] Split(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var categories = new List<FileCategoryDefinition>
        {
            new(1, "Email", "Mail stores and messages",
                Split("pst, ost, msg, eml, emlx, mbox, mbx, nsf, olm, dbx, oft, p7m")),
            new(2, "Chat", "Chat and short-message formats",
                Split("rsmf")),
            new(3, "Calendar & Contacts", "Calendar items and contact cards",
                Split("ics, ical, vcs, vcf")),
            new(4, "Word Processing", "Word processing documents",
                Split("doc, docx, docm, dot, dotx, dotm, rtf, odt, ott, wpd, wps, pages, hwp, lwp")),
            new(5, "Spreadsheets", "Spreadsheets and delimited data",
                Split("xls, xlsx, xlsm, xlsb, xlt, xltx, xltm, csv, tsv, ods, ots, numbers, wk1, wk3, wk4, wks, 123, qpw")),
            new(6, "Presentations", "Slide decks",
                Split("ppt, pptx, pptm, pps, ppsx, ppsm, pot, potx, potm, odp, otp, key")),
            new(7, "PDF & Fixed Layout", "PDF and XPS documents",
                Split("pdf, xps, oxps")),
            new(8, "Other Office Documents", "OneNote, Visio, Project, Publisher",
                Split("one, onepkg, onetoc2, vsd, vsdx, vsdm, mpp, pub")),
            new(9, "Text & Web", "Plain text, logs, markup and web pages",
                Split("txt, log, md, xml, json, yaml, yml, htm, html, mht, mhtml, css")),
            new(10, "Images", "Photos, graphics and camera raw files",
                Split("jpg, jpeg, jpe, png, gif, bmp, tif, tiff, heic, heif, webp, svg, ico, psd, ai, eps, emf, wmf, raw, cr2, cr3, nef, arw, dng, orf")),
            new(11, "Audio", "Audio recordings and music",
                Split("mp3, wav, wma, m4a, aac, flac, ogg, oga, opus, aif, aiff, amr, mid, midi")),
            new(12, "Video", "Video recordings",
                Split("mp4, m4v, mov, avi, wmv, mkv, flv, webm, mpg, mpeg, 3gp, 3g2, mts, m2ts, vob, ts")),
            new(13, "Archives & Containers", "Compressed archives and disc images",
                Split("zip, zipx, 7z, rar, tar, gz, tgz, bz2, xz, z, cab, iso, lzh, arj")),
            new(14, "Forensic Images", "Forensic and mobile acquisition images, split parts",
                Split("ex01, lx01, ad1, aff, aff4, dd, img, dmg, vhd, vhdx, vmdk, ufd, ufdr, ufdx"),
                [.. Range("e", 1, 99, 2), .. Range("l", 1, 99, 2), .. Range("", 1, 999, 3)]),
            new(15, "Databases", "Database files",
                Split("db, sqlite, sqlite3, mdb, accdb, dbf, sdf, mdf, ldf, ndf, frm, myd, ibd")),
            new(16, "CAD & Engineering", "Drawings and 3D models",
                Split("dwg, dxf, dgn, rvt, skp, stl, step, stp, iges, igs, sldprt, sldasm")),
            new(17, "Source Code & Scripts", "Program source and scripts",
                Split("c, cpp, h, hpp, cs, java, py, js, vb, vbs, ps1, bat, cmd, sh, sql, php, rb, go, pl, swift, kt")),
            new(18, "System Files", "Executables, libraries and operating system files",
                Split("exe, dll, sys, msi, msp, drv, ocx, cpl, scr, mui, lnk, tmp, ini, inf, cat, pf, evtx, etl, reg, manifest, pdb, ds_store")),
            new(NoExtensionId, "No Extension", "Files without an extension", []),
            new(OtherUnknownId, "Other / Unknown", "Extensions not in the category list", []),
        };

        return categories;
    }

    private static IEnumerable<string> Range(string prefix, int from, int to, int digits) =>
        Enumerable.Range(from, to - from + 1).Select(i => prefix + i.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture));

    private static Dictionary<string, int> BuildMap(IReadOnlyList<FileCategoryDefinition> categories)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            foreach (var extension in category.Extensions)
            {
                if (!map.TryAdd(extension, category.CategoryId))
                {
                    throw new InvalidOperationException(
                        $"Extension '{extension}' is listed in more than one category ({map[extension]} and {category.CategoryId}).");
                }
            }
        }

        foreach (var category in categories)
        {
            foreach (var extension in category.SplitPartExtensions ?? [])
            {
                map.TryAdd(extension, category.CategoryId);
            }
        }

        return map;
    }
}

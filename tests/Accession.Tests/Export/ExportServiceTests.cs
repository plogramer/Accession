using System.Globalization;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Settings;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.Export;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Export;

/// <summary>Export content (EXP-01…EXP-06): sheets, scopes, row counts, splitting, per-media workbooks, cancel and audit.</summary>
public sealed class ExportServiceTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly ExportService _export;
    private readonly long _m1;
    private readonly long _m2;

    public ExportServiceTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            _m2 = media.Insert("123-123_002", @"\123-123_002\", _test.Time.GetUtcNow(), "u");
            _m1 = media.Insert("123-123_001", @"\123-123_001\", _test.Time.GetUtcNow(), "u");
            var root1 = ScanRows.AddFolder(scope, _m1, @"\123-123_001\");
            var mail = ScanRows.AddFolder(scope, _m1, @"\123-123_001\mail\", root1);
            var root2 = ScanRows.AddFolder(scope, _m2, @"\123-123_002\");
            for (var i = 0; i < 2_500; i++)
            {
                ScanRows.AddFile(scope, _m1, i < 400 ? mail : root1, $"doc_{i:0000}.{(i % 2 == 0 ? "msg" : "pdf")}", size: i * 10,
                    sha1: i < 10 ? new string('a', 40) : null);
            }

            for (var i = 0; i < 300; i++)
            {
                ScanRows.AddFile(scope, _m2, root2, $"copy_{i:000}.xlsx", size: 5);
            }

            scope.Connection.Execute("UPDATE File SET ModifiedUtc = '2021-06-01T12:30:00.0000000Z' WHERE Name = 'doc_0000.msg'", transaction: scope.Transaction);
            var errors = new ScanErrorRepository(scope);
            for (var i = 0; i < 7; i++)
            {
                errors.Insert(new ScanErrorEntry
                {
                    MediaKey = i < 5 ? _m1 : _m2, RelativePath = $@"\x\locked{i}.pst", ItemType = ScanItemType.File,
                    ErrorType = ScanErrorType.FileLocked, ErrorCode = 32, Message = "In use", OccurredAtUtc = _test.Time.GetUtcNow(),
                });
            }

            transaction.Commit();
        }

        _export = new ExportService(new FileBrowserQueries(), new DashboardQueries(null, NullLogger<DashboardQueries>.Instance),
            _test.Factory, new App(), NullLogger<ExportService>.Instance);
    }

    public void Dispose() => _test.Dispose();

    private string Out(string name = "ACME_2026-001_Inventory_20260927.xlsx") => _test.Temp.Combine("out", name);

    [Fact]
    public void All_sheets_hold_the_inventory_and_the_export_is_audited()
    {
        var result = _export.Export(_test.Session, new ExportRequest { OutputPath = Out() });

        var sheets = XlsxReader.Read(Out());
        Assert.Equal(["Summary", "Media", "Categories", "Extensions", "Files", "Errors"], sheets.Select(s => s.Name));
        Assert.Equal(2_800, sheets.Data("Files").Count); // equals the database
        Assert.Equal(7, sheets.Data("Errors").Count);
        Assert.Equal(["123-123_001", "123-123_002"], sheets.Data("Media").Select(r => r[0])); // Media ID order

        var summary = sheets.Data("Summary").ToDictionary(r => r[0], r => r.ElementAtOrDefault(1) ?? string.Empty);
        Assert.Equal("ACME Corporation", summary["Client"]);
        Assert.Equal("2026-001", summary["Matter ID"]);
        Assert.Equal("All media", summary["Scope"]);
        Assert.Equal(@"CORP\jdoe", summary["Exported by"]);
        Assert.StartsWith("Decimal", summary["Size units"], StringComparison.Ordinal);

        var header = sheets.Single(s => s.Name == "Files").Rows[0];
        Assert.Equal(["Media ID", "Relative Path", "File Name", "Extension", "Category", "Size (bytes)", "Size",
            "Created (UTC)", "Modified (UTC)", "Accessed (UTC)", "SHA-1"], header);
        var first = sheets.Data("Files")[0];
        Assert.Equal(["123-123_001", @"\123-123_001\"], first.Take(2)); // root folder first, then by name
        var dated = sheets.Data("Files").Single(r => r[2] == "doc_0000.msg");
        Assert.Equal(new DateTime(2021, 6, 1, 12, 30, 0), DateTime.FromOADate(double.Parse(dated[8], CultureInfo.InvariantCulture)), TimeSpan.FromSeconds(1));
        Assert.Equal("Email", dated[4]);

        Assert.Equal(sheets.Sum(s => s.Rows.Count - 1), result.TotalRows); // the result's row counts match the workbook
        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.ExportCreated] }).Single();
        Assert.Contains("\"name\":\"Files\",\"rows\":2800", audit.Details, StringComparison.Ordinal);
        Assert.Contains("ACME_2026-001_Inventory_20260927.xlsx", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_media_and_selected_sheets_only()
    {
        _export.Export(_test.Session, new ExportRequest
        {
            OutputPath = Out(), MediaKeys = [_m2], Sheets = new HashSet<ExportSheet> { ExportSheet.Files, ExportSheet.Errors },
        });

        var sheets = XlsxReader.Read(Out());
        Assert.Equal(["Files", "Errors"], sheets.Select(s => s.Name));
        Assert.Equal(300, sheets.Data("Files").Count);
        Assert.All(sheets.Data("Files"), r => Assert.Equal("123-123_002", r[0]));
        Assert.Equal(2, sheets.Data("Errors").Count);
    }

    [Fact]
    public void Current_files_view_exports_exactly_the_filtered_files()
    {
        _export.Export(_test.Session, new ExportRequest
        {
            OutputPath = Out(), FilesView = new FileFilter { Extension = "msg", MediaKey = _m1 },
            Sheets = new HashSet<ExportSheet> { ExportSheet.Summary, ExportSheet.Files },
        });

        var sheets = XlsxReader.Read(Out());
        Assert.Equal(1_250, sheets.Data("Files").Count);
        Assert.All(sheets.Data("Files"), r => Assert.Equal("msg", r[3]));
        var summary = sheets.Data("Summary").ToDictionary(r => r[0], r => r.ElementAtOrDefault(1) ?? string.Empty);
        Assert.Equal("1250", summary["Files in this view"]);
        Assert.Equal("Current Files view (media 123-123_001)", summary["Scope"]);
    }

    [Fact]
    public void Large_file_lists_split_into_numbered_sheets_as_estimated()
    {
        var request = new ExportRequest { OutputPath = Out(), MaxRowsPerSheet = 1_000 };

        var estimate = _export.Estimate(_test.Session, request);
        Assert.Equal(2_800, estimate.FileRows);
        Assert.Equal(7, estimate.ErrorRows);
        Assert.Equal(3, estimate.FilesSheets);

        _export.Export(_test.Session, request);
        var sheets = XlsxReader.Read(Out());
        Assert.Equal(["Files (1)", "Files (2)", "Files (3)"], sheets.Select(s => s.Name).Where(n => n.StartsWith("Files", StringComparison.Ordinal)));
        Assert.Equal(800, sheets.Data("Files (3)").Count);
    }

    [Fact]
    public void One_workbook_per_media()
    {
        var result = _export.Export(_test.Session, new ExportRequest { OutputPath = Out("export.xlsx"), OneWorkbookPerMedia = true });

        Assert.Equal([Out("export_123-123_001.xlsx"), Out("export_123-123_002.xlsx")], result.Workbooks.Select(w => w.Path));
        Assert.Equal(2_500, XlsxReader.Read(Out("export_123-123_001.xlsx")).Data("Files").Count);
        Assert.Equal(300, XlsxReader.Read(Out("export_123-123_002.xlsx")).Data("Files").Count);
        Assert.False(File.Exists(Out("export.xlsx")));
    }

    [Fact]
    public void Cancel_removes_every_workbook_of_the_export()
    {
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.Stage.Contains("_002", StringComparison.Ordinal))
            {
                cancel.Cancel(); // the first workbook is already finished
            }
        });

        Assert.ThrowsAny<OperationCanceledException>(() => _export.Export(_test.Session,
            new ExportRequest { OutputPath = Out("export.xlsx"), OneWorkbookPerMedia = true }, progress, cancel.Token));

        Assert.Empty(Directory.GetFiles(_test.Temp.Combine("out")));
        Assert.Empty(_test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.ExportCreated] }));
    }

    [Fact]
    public void Audit_log_export_holds_the_filtered_entries_and_is_audited()
    {
        _test.Session.Audit.Write(AuditAction.MediaDeleted, "123-123_009", new { Reason = "test" });

        var workbook = _export.ExportAuditLog(_test.Session, new AuditQuery { Actions = [AuditAction.MediaDeleted] }, Out("audit.xlsx"));

        var rows = XlsxReader.Read(Out("audit.xlsx")).Data("Audit Log");
        var row = Assert.Single(rows);
        Assert.Equal(["Media deleted", "123-123_009"], row.Skip(3).Take(2));
        Assert.Equal(1, workbook.TotalRows);
        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.ExportCreated] }).Single();
        Assert.Contains("\"scope\":\"AuditLog\"", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Readable_sizes_follow_the_unit_setting()
    {
        _export.Export(_test.Session, new ExportRequest
        {
            OutputPath = Out(), SizeUnit = SizeUnitSystem.Binary, Sheets = new HashSet<ExportSheet> { ExportSheet.Files },
        });

        var row = XlsxReader.Read(Out()).Data("Files").Single(r => r[2] == "doc_2047.pdf");
        Assert.Equal("20470", row[5]);
        Assert.Equal("19.99 KiB", row[6]);
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class SyncProgress(Action<ExportProgress> report) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => report(value);
    }
}

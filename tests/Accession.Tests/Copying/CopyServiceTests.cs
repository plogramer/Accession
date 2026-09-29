using System.Text;
using Accession.Core.Copying;
using Accession.Core.Model;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.Copying;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Copying;

/// <summary>Copy batch and in-app copy (requirements 5.8b): order, naming, .bat content, manifest, skip/fail/cancel, audit.</summary>
public sealed class CopyServiceTests : IDisposable
{
    private static readonly DateTime Modified = new(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly TestSession _test = new();
    private readonly CopyService _copy;
    private readonly Dictionary<string, long> _ids = [];

    public CopyServiceTests()
    {
        // MED002 is added first: copy order is by Media ID, not by insertion.
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope);
            var m2 = media.Insert("MED002", @"\MED002\", _test.Time.GetUtcNow(), "u");
            var m1 = media.Insert("MED001", @"\MED001\", _test.Time.GetUtcNow(), "u");
            var root1 = ScanRows.AddFolder(scope, m1, @"\MED001\");
            var mail = ScanRows.AddFolder(scope, m1, @"\MED001\mail\", root1);
            var root2 = ScanRows.AddFolder(scope, m2, @"\MED002\");
            Add(scope, m1, mail, @"\MED001\mail\", "résumé.docx");
            Add(scope, m1, mail, @"\MED001\mail\", "100% & done.msg");
            Add(scope, m1, root1, @"\MED001\", "b.txt");
            Add(scope, m1, root1, @"\MED001\", "A.PDF");
            Add(scope, m2, root2, @"\MED002\", "noext");
            Dapper.SqlMapper.Execute(scope.Connection, "UPDATE File SET ModifiedUtc = '2019-01-02T03:04:05.0000000Z'", transaction: scope.Transaction);
            transaction.Commit();
        }

        Directory.SetLastWriteTimeUtc(Path.Combine(_test.Root, "MED001", "mail"), Modified);
        _copy = new CopyService(_test.Factory, NullLogger<CopyService>.Instance);
    }

    public void Dispose() => _test.Dispose();

    private void Add(Accession.Data.DbScope scope, long media, long folder, string folderPath, string name)
    {
        var content = Encoding.UTF8.GetBytes("content of " + name);
        var path = CopyPaths.Combine(_test.Root, folderPath + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, Modified);
        _ids[name] = ScanRows.AddFile(scope, media, folder, name, content.Length);
    }

    private string Out(params string[] parts) => _test.Temp.Combine(["out", .. parts]);

    private CopyRequest Request(CopyNaming? naming = null, FileFilter? filter = null) =>
        new(filter ?? FileFilter.None, Out("dest"), naming ?? CopyNaming.Preserve, Out("manifest.csv")) { ScopeText = "All results" };

    private static readonly CopyNaming Sequential = new() { Mode = CopyNamingMode.Sequential, Prefix = "DOC", Digits = 4, StartNumber = 1 };

    private static List<string[]> Manifest(string? path) =>
        [.. File.ReadAllLines(path ?? throw new ArgumentNullException(nameof(path)), Encoding.UTF8).Skip(1).Select(line => line[1..^1].Split("\",\"").Select(f => f.Replace("\"\"", "\"", StringComparison.Ordinal)).ToArray())];

    [Fact]
    public void Sequential_names_follow_media_folder_name_order()
    {
        var result = _copy.GenerateBatch(_test.Session, Request(Sequential), CopyCommandTemplate.Copy, Out("copy.bat"));

        Assert.Equal(5, result.Files);
        var rows = Manifest(result.ManifestPath);
        Assert.Equal(["DOC0001.PDF", "DOC0002.txt", "DOC0003.msg", "DOC0004.docx", "DOC0005"], rows.Select(r => Path.GetFileName(r[1])));
        Assert.Equal(["A.PDF", "b.txt", "100% & done.msg", "résumé.docx", "noext"], rows.Select(r => Path.GetFileName(r[2])));
        Assert.All(rows, r => Assert.Equal("In batch", r[7]));
        Assert.Equal(["1", "2", "3", "4", "5"], rows.Select(r => r[0]));
        Assert.Equal(CopyPaths.Combine(_test.Root, @"\MED001\A.PDF"), rows[0][2]);
        Assert.Equal("MED001", rows[0][3]);
        Assert.Equal("2019-01-02 03:04:05", rows[0][5]);
    }

    [Fact]
    public void The_batch_skips_existing_files_counts_failures_and_escapes_percent()
    {
        var result = _copy.GenerateBatch(_test.Session, Request(), CopyCommandTemplate.Copy, Out("copy.bat"));

        var bat = File.ReadAllText(result.BatchPath, Encoding.UTF8);
        Assert.StartsWith("@echo off\r\n", bat, StringComparison.Ordinal);
        Assert.False(File.ReadAllBytes(result.BatchPath).AsSpan().StartsWith(Encoding.UTF8.Preamble)); // a BOM would break the first line
        Assert.Contains("chcp 65001 >nul", bat, StringComparison.Ordinal);
        Assert.Contains("rem Copy batch written by Accession on 2026-09-27 09:00 UTC by CORP\\jdoe.", bat, StringComparison.Ordinal);

        var destination = CopyPaths.Combine(Out("dest"), @"\MED001\mail\100% & done.msg").Replace("%", "%%", StringComparison.Ordinal);
        var source = CopyPaths.Combine(_test.Root, @"\MED001\mail\100% & done.msg").Replace("%", "%%", StringComparison.Ordinal);
        Assert.Contains($"if exist \"{destination}\" (set /a SKIPPED+=1 & echo Skipped, already exists: \"{destination}\") else (\r\n" +
                        $"  copy /Y \"{source}\" \"{destination}\" >nul\r\n" +
                        $"  if errorlevel 1 (set /a FAILED+=1 & echo FAILED: \"{source}\") else (set /a COPIED+=1)\r\n)", bat, StringComparison.Ordinal);

        // One mkdir per destination folder, in order.
        var mkdirs = bat.Split("\r\n").Where(l => l.StartsWith("if not exist", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, mkdirs.Count);
        Assert.Contains($"mkdir \"{CopyPaths.Combine(Out("dest"), @"\MED001\mail\")}\"", mkdirs[1], StringComparison.Ordinal);
        Assert.EndsWith("endlocal & exit /b 0\r\n", bat, StringComparison.Ordinal);
        Assert.False(File.Exists(result.BatchPath + ".partial"));
        Assert.False(Directory.Exists(Out("dest"))); // generating a batch copies nothing

        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.CopyBatchGenerated] }).Single();
        Assert.Contains("\"files\":5", audit.Details, StringComparison.Ordinal);
        Assert.Contains("copy.bat", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Robocopy_checks_exit_code_8_and_refuses_sequential_names()
    {
        var result = _copy.GenerateBatch(_test.Session, Request(), CopyCommandTemplate.Robocopy, Out("robo.bat"));
        Assert.Contains("  if errorlevel 8 (set /a FAILED+=1", File.ReadAllText(result.BatchPath), StringComparison.Ordinal);

        var error = Assert.Throws<ArgumentException>(() => _copy.GenerateBatch(_test.Session, Request(Sequential), CopyCommandTemplate.Robocopy, Out("robo2.bat")));
        Assert.Contains("robocopy cannot rename", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Out("robo2.bat")));
    }

    [Fact]
    public void Nothing_is_ever_written_under_the_root()
    {
        var inside = Request() with { Destination = Path.Combine(_test.Root, "MED001", "out") };
        Assert.Throws<ArgumentException>(() => _copy.CopyFiles(_test.Session, inside, new CopyFileOptions()));
        Assert.Throws<ArgumentException>(() => _copy.GenerateBatch(_test.Session, Request(), CopyCommandTemplate.Copy, Path.Combine(_test.Root, "x.bat")));
        Assert.Throws<ArgumentException>(() => _copy.CopyFiles(_test.Session, Request() with { ManifestPath = Path.Combine(_test.Root, "m.csv") }, new CopyFileOptions()));
        Assert.False(Directory.Exists(Path.Combine(_test.Root, "MED001", "out")));
    }

    [Fact]
    public void Copy_keeps_the_structure_and_metadata_and_skips_existing_files()
    {
        var existing = CopyPaths.Combine(Out("dest"), @"\MED001\b.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "already here");
        File.Delete(CopyPaths.Combine(_test.Root, @"\MED002\noext")); // gone since the scan
        var reports = new List<CopyProgress>();

        var result = _copy.CopyFiles(_test.Session, Request(), new CopyFileOptions(Verify: true), new Collect(reports));

        Assert.Equal((5L, 3L, 3L, 1L, 1L, false), (result.TotalFiles, result.Copied, result.Verified, result.Skipped, result.Failed, result.Cancelled));
        Assert.Equal("already here", File.ReadAllText(existing));
        var copied = CopyPaths.Combine(Out("dest"), @"\MED001\mail\100% & done.msg");
        Assert.Equal("content of 100% & done.msg", File.ReadAllText(copied));
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(copied));
        Assert.Equal(Modified, Directory.GetLastWriteTimeUtc(Path.GetDirectoryName(copied)!)); // folder times too
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(CopyPaths.Combine(_test.Root, @"\MED001\mail\100% & done.msg"))); // source untouched
        Assert.Equal(5, reports[^1].FilesDone);

        var rows = Manifest(result.ManifestPath);
        Assert.Equal(["Verified", "Skipped", "Verified", "Verified", "Failed"], rows.Select(r => r[7]));
        Assert.False(string.IsNullOrEmpty(rows[4][8]));

        var audit = _test.Session.Audit.Query(new AuditQuery { Actions = [AuditAction.FilesCopied] }).Single();
        Assert.Contains("\"copied\":3", audit.Details, StringComparison.Ordinal);
        Assert.Contains("\"verify\":true", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticked_files_only_with_sequential_names()
    {
        var filter = FileFilter.None with { FileIds = [_ids["résumé.docx"], _ids["noext"]] };

        var result = _copy.CopyFiles(_test.Session, Request(Sequential with { StartNumber = 41 }, filter), new CopyFileOptions());

        Assert.Equal(2, result.Copied);
        Assert.Equal(["DOC0041.docx", "DOC0042", "manifest.csv"], Directory.GetFiles(Out("dest")).Concat(Directory.GetFiles(Out())).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("content of noext", File.ReadAllText(Out("dest", "DOC0042")));
    }

    [Fact]
    public void Cancel_keeps_what_was_copied_and_the_manifest_says_where_it_stopped()
    {
        using var cancel = new CancellationTokenSource();
        var progress = new Collect([], p =>
        {
            if (p.FilesDone == 2)
            {
                cancel.Cancel();
            }
        });

        var result = _copy.CopyFiles(_test.Session, Request(), new CopyFileOptions(), progress, cancel.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Copied);
        Assert.Equal(3, result.NotReached);
        var rows = Manifest(result.ManifestPath);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Stopped", rows[2][7]);
        Assert.True(File.Exists(CopyPaths.Combine(Out("dest"), @"\MED001\b.txt")));
    }

    private static string Sha1Of(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(text)));

    private void SetSha1(string name, string sha1)
    {
        using var scope = _test.Session.Database.Open();
        Dapper.SqlMapper.Execute(scope.Connection, "UPDATE File SET Sha1 = @sha1, HashStatus = 1 WHERE FileId = @id", new { sha1, id = _ids[name] });
    }

    private static readonly CopyNaming Sha1Names = new() { Mode = CopyNamingMode.Sha1Name };

    [Fact]
    public void Sha1_names_are_flat_computed_when_missing_and_flag_changed_files()
    {
        SetSha1("b.txt", Sha1Of("content of b.txt"));
        SetSha1("A.PDF", new string('0', 40)); // not what the file holds any more

        var result = _copy.CopyFiles(_test.Session, Request(Sha1Names), new CopyFileOptions());

        Assert.Equal((5L, 0L, 0L), (result.Copied, result.Skipped, result.Failed));
        var expected = new[] { "A.PDF", "b.txt", "100% & done.msg", "résumé.docx", "noext" }.Select(n => Sha1Of("content of " + n) + "_" + n);
        Assert.Equal(expected.Order(StringComparer.Ordinal), Directory.GetFileSystemEntries(Out("dest")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var rows = Manifest(result.ManifestPath);
        Assert.Contains("changed after the scan", rows[0][8], StringComparison.Ordinal);
        Assert.Equal(Out("dest", Sha1Of("content of noext") + "_noext"), rows[4][1]);
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(rows[1][1]));

        // Again: everything is there already (hashed ones are not even read), and no temporary file is left behind.
        var again = _copy.CopyFiles(_test.Session, Request(Sha1Names) with { ManifestPath = Out("m2.csv") }, new CopyFileOptions());
        Assert.Equal((0L, 5L), (again.Copied, again.Skipped));
        Assert.Equal(5, Directory.GetFileSystemEntries(Out("dest")).Length);
    }

    [Fact]
    public void A_batch_with_sha1_names_leaves_out_files_without_a_sha1()
    {
        SetSha1("b.txt", Sha1Of("content of b.txt"));

        var result = _copy.GenerateBatch(_test.Session, Request(Sha1Names), CopyCommandTemplate.Copy, Out("sha.bat"));

        Assert.Equal((1L, 4L), (result.Files, result.NotInBatch));
        Assert.Contains(Sha1Of("content of b.txt") + "_b.txt", File.ReadAllText(result.BatchPath), StringComparison.Ordinal);
        var rows = Manifest(result.ManifestPath);
        Assert.Equal(["Not in batch", "In batch", "Not in batch", "Not in batch", "Not in batch"], rows.Select(r => r[7]));
        Assert.Throws<ArgumentException>(() => _copy.GenerateBatch(_test.Session, Request(Sha1Names), CopyCommandTemplate.Robocopy, Out("r.bat")));
    }

    /// <summary>Synchronous progress (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class Collect(List<CopyProgress> reports, Action<CopyProgress>? then = null) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value)
        {
            reports.Add(value);
            then?.Invoke(value);
        }
    }
}

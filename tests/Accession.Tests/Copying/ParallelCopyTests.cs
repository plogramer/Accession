using System.Text;
using Accession.Core.Copying;
using Accession.Core.Settings;
using Accession.Data.Browsing;
using Accession.Data.Copying;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Copying;

/// <summary>Copy files and Quick Copy with several threads (Settings → Copy threads): same result as one thread, manifest in copy order.</summary>
public sealed class ParallelCopyTests : IDisposable
{
    private const int Folders = 6;
    private const int FilesPerFolder = 40;

    private readonly TestSession _test = new();
    private readonly CopyService _copy;

    public ParallelCopyTests()
    {
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            var media = new MediaRepository(scope).Insert("MED001", @"\MED001\", _test.Time.GetUtcNow(), "u");
            var root = ScanRows.AddFolder(scope, media, @"\MED001\");
            for (var f = 0; f < Folders; f++)
            {
                var folderPath = $@"\MED001\f{f}\";
                var folder = ScanRows.AddFolder(scope, media, folderPath, root);
                for (var i = 0; i < FilesPerFolder; i++)
                {
                    // Every folder has a "same.txt" with the same content: SHA-1 names copy it once.
                    var name = i == 0 ? "same.txt" : $"doc{i:000}.txt";
                    var content = i == 0 ? "identical" : $"content {f}/{i}";
                    var path = CopyPaths.Combine(_test.Root, folderPath + name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                    ScanRows.AddFile(scope, media, folder, name, Encoding.UTF8.GetByteCount(content));
                }
            }

            transaction.Commit();
        }

        _copy = new CopyService(_test.Factory, NullLogger<CopyService>.Instance);
    }

    public void Dispose() => _test.Dispose();

    private CopyRequest Request(CopyNaming naming, int threads, string name = "dest") =>
        new(FileFilter.None, _test.Temp.Combine(name), naming, _test.Temp.Combine(name + ".csv")) { Threads = threads };

    private static List<string[]> Manifest(string path) =>
        [.. File.ReadAllLines(path, Encoding.UTF8).Skip(1).Select(line => line[1..^1].Split("\",\""))];

    [Fact]
    public void Several_threads_copy_everything_with_the_manifest_in_copy_order()
    {
        var naming = new CopyNaming { Mode = CopyNamingMode.Sequential, Prefix = "P", Digits = 5 };

        var one = _copy.CopyFiles(_test.Session, Request(naming, threads: 1, "one"), new CopyFileOptions(Verify: true));
        var many = _copy.CopyFiles(_test.Session, Request(naming, threads: 8, "many"), new CopyFileOptions(Verify: true));

        const int total = Folders * FilesPerFolder;
        Assert.Equal((total, (long)total, 0L), (many.TotalFiles, many.Copied, many.Failed));
        Assert.Equal(one.BytesCopied, many.BytesCopied);
        var rowsOne = Manifest(one.ManifestPath);
        var rowsMany = Manifest(many.ManifestPath);
        Assert.Equal(Enumerable.Range(1, total).Select(n => n.ToString()), rowsMany.Select(r => r[0])); // in order
        Assert.Equal(rowsOne.Select(r => (Path.GetFileName(r[1]), r[2])), rowsMany.Select(r => (Path.GetFileName(r[1]), r[2]))); // same names
        foreach (var row in rowsMany)
        {
            Assert.Equal(File.ReadAllText(row[2]), File.ReadAllText(row[1]));
        }
    }

    [Fact]
    public void Quick_copy_puts_everything_in_one_folder_with_original_names_numbered_when_taken()
    {
        var request = Request(new CopyNaming { Mode = CopyNamingMode.OriginalName }, threads: 8, "quick");
        Directory.CreateDirectory(request.Destination);
        File.WriteAllText(Path.Combine(request.Destination, "same.txt"), "already there");

        var result = _copy.CopyFiles(_test.Session, request, new CopyFileOptions(PreserveMetadata: false));

        const int total = Folders * FilesPerFolder;
        Assert.Equal((total, (long)total, 0L, 0L), (result.TotalFiles, result.Copied, result.Skipped, result.Failed));
        Assert.Empty(Directory.GetDirectories(request.Destination)); // flat
        Assert.Equal("already there", File.ReadAllText(Path.Combine(request.Destination, "same.txt"))); // never overwritten

        // Each folder's same.txt, in copy order: same_2_.txt … same_7_.txt (same.txt was taken).
        var rows = Manifest(result.ManifestPath);
        Assert.Equal(Enumerable.Range(2, Folders).Select(n => $"same_{n}_.txt"),
            rows.Where(r => Path.GetFileName(r[2]) == "same.txt").Select(r => Path.GetFileName(r[1])));
        Assert.All(rows, r => Assert.Equal(File.ReadAllText(r[2]), File.ReadAllText(r[1])));

        // doc001.txt exists in every folder too.
        Assert.True(File.Exists(Path.Combine(request.Destination, "doc001.txt")));
        Assert.True(File.Exists(Path.Combine(request.Destination, $"doc001_{Folders}_.txt")));
        Assert.Equal(total + 1, Directory.GetFiles(request.Destination).Length);
    }

    [Fact]
    public void Sha1_names_copy_identical_files_once_even_across_threads()
    {
        var result = _copy.CopyFiles(_test.Session, Request(new CopyNaming { Mode = CopyNamingMode.Sha1Name }, threads: 8), new CopyFileOptions());

        Assert.Equal(0, result.Failed);
        Assert.Equal(Folders - 1, result.Skipped); // five of the six "same.txt"
        Assert.Equal(Folders * (FilesPerFolder - 1) + 1, Directory.GetFiles(_test.Temp.Combine("dest")).Length);
        Assert.Empty(Directory.GetFiles(_test.Temp.Combine("dest"), "*.partial"));
    }

    [Fact]
    public void Cancel_with_several_threads_keeps_what_was_copied_and_leaves_no_partial_files()
    {
        using var cancel = new CancellationTokenSource();
        var progress = new Synchronous(p =>
        {
            if (p.FilesDone >= 30)
            {
                cancel.Cancel();
            }
        });

        var result = _copy.CopyFiles(_test.Session, Request(CopyNaming.Preserve, threads: 4), new CopyFileOptions(), progress, cancel.Token);

        Assert.True(result.Cancelled);
        Assert.InRange(result.Copied, 30, Folders * FilesPerFolder - 1);
        var rows = Manifest(result.ManifestPath);
        Assert.Equal("Stopped", rows[^1][7]);
        Assert.Equal(result.Copied, rows.Count - 1); // every copied file is listed
        Assert.Equal(result.Copied, Directory.GetFiles(_test.Temp.Combine("dest"), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Copy_threads_setting_defaults_to_four_and_is_kept_in_range()
    {
        Assert.Equal(4, new AppSettings().CopyThreads);
        var settings = new AppSettings { CopyThreads = 99 };
        settings.Normalize();
        Assert.Equal(SettingsLimits.MaxCopyThreads, settings.CopyThreads);
    }

    private sealed class Synchronous(Action<CopyProgress> report) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => report(value);
    }
}

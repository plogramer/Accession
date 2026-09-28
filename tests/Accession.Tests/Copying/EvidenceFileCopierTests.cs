using Accession.Core.Copying;
using Accession.Tests.TestSupport;

namespace Accession.Tests.Copying;

/// <summary>Copying one file (CPY-06): bytes, metadata, verify, skip existing, failures and cancel.</summary>
public sealed class EvidenceFileCopierTests : IDisposable
{
    private static readonly DateTime Created = new(2018, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime Modified = new(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime Accessed = new(2020, 7, 8, 9, 10, 11, DateTimeKind.Utc);

    private readonly TempDirectory _temp = new();
    private readonly EvidenceFileCopier _copier = new();

    public void Dispose() => _temp.Dispose();

    private string Source(string name, byte[] content)
    {
        var path = _temp.Combine("src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetCreationTimeUtc(path, Created);
        File.SetLastWriteTimeUtc(path, Modified);
        File.SetLastAccessTimeUtc(path, Accessed);
        return path;
    }

    [Fact]
    public void Copies_the_bytes_and_keeps_the_times_and_attributes()
    {
        var content = new byte[3 * EvidenceFileCopier.BufferSize + 17];
        Random.Shared.NextBytes(content);
        var source = Source("résumé 100%.bin", content);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(source, FileAttributes.Hidden);
        }

        var destination = _temp.Combine("out", "a", "b", "copy.bin");
        long reported = 0;

        var result = _copier.Copy(source, destination, new CopyFileOptions(), read => reported += read);

        Assert.Equal(CopyOutcome.Copied, result.Outcome);
        Assert.Equal(Accessed, File.GetLastAccessTimeUtc(destination), TimeSpan.FromSeconds(2)); // before reading it
        Assert.Null(result.Message);
        Assert.Equal(content.Length, result.Bytes);
        Assert.Equal(content.Length, reported);
        Assert.Equal(content, File.ReadAllBytes(destination));
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(destination));
        Assert.Equal(Modified, File.GetLastWriteTimeUtc(source)); // the source is untouched
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Created, File.GetCreationTimeUtc(destination));
            Assert.True(File.GetAttributes(destination).HasFlag(FileAttributes.Hidden));
        }
    }

    [Fact]
    public void Without_preserve_metadata_the_copy_gets_new_times()
    {
        var source = Source("a.txt", [1, 2, 3]);
        var destination = _temp.Combine("out", "a.txt");

        _copier.Copy(source, destination, new CopyFileOptions(PreserveMetadata: false));

        Assert.NotEqual(Modified, File.GetLastWriteTimeUtc(destination));
    }

    [Fact]
    public void Verify_reads_the_copy_back()
    {
        var source = Source("a.txt", "hello"u8.ToArray());

        var result = _copier.Copy(source, _temp.Combine("out", "a.txt"), new CopyFileOptions(Verify: true));

        Assert.Equal(CopyOutcome.Verified, result.Outcome);
        Assert.Equal("aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d", result.Sha1);
    }

    [Fact]
    public void An_existing_file_is_skipped_and_left_as_it_is()
    {
        var source = Source("a.txt", [1, 2, 3]);
        var destination = _temp.Combine("out", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, [9]);

        var result = _copier.Copy(source, destination, new CopyFileOptions());

        Assert.Equal(CopyOutcome.Skipped, result.Outcome);
        Assert.Equal([9], File.ReadAllBytes(destination));
    }

    [Fact]
    public void A_missing_source_fails_without_leaving_a_file()
    {
        var destination = _temp.Combine("out", "gone.txt");

        var result = _copier.Copy(_temp.Combine("src", "gone.txt"), destination, new CopyFileOptions());

        Assert.Equal(CopyOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Message));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Cancel_deletes_the_partial_copy()
    {
        var source = Source("big.bin", new byte[3 * EvidenceFileCopier.BufferSize]);
        var destination = _temp.Combine("out", "big.bin");
        using var cancel = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() => _copier.Copy(source, destination, new CopyFileOptions(), _ => cancel.Cancel(), cancel.Token));

        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Long_paths_work()
    {
        var deep = string.Join(Path.DirectorySeparatorChar, Enumerable.Range(0, 12).Select(i => $"folder_with_a_long_name_{i:00}"));
        var source = Source(Path.Combine(deep, "file.txt"), [1]);
        var destination = _temp.Combine("out", deep, "file.txt");
        Assert.True(destination.Length > 300);

        var result = _copier.Copy(source, destination, new CopyFileOptions(Verify: true));

        Assert.Equal(CopyOutcome.Verified, result.Outcome);
        Assert.True(File.Exists(destination));
    }
}

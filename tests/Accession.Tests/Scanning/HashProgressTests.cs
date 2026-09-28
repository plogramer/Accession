using Accession.Core.Scanning;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Scanning;

/// <summary>
/// Progress inside large files: a 500 GB file used to show 0 B/s and 0 % until it was done, and "Now" showed whichever
/// small file another thread started last, so a working scan looked stuck.
/// </summary>
public sealed class HashProgressTests
{
    [Fact]
    public void Bytes_count_while_reading_and_the_total_ends_at_the_file_size()
    {
        var counters = new ScanCounters();
        counters.AddFiles(2, 1_000 + 10);

        var big = counters.BeginHashing(1, @"\M1\disk.vhdx", 1_000);
        counters.AddHashedBytes(big, 300);
        Assert.Equal(300, counters.BytesHashed); // moving before the file is done
        Assert.Equal(0, counters.FilesHashed);

        counters.RestartHashing(big); // a network retry reads it again
        Assert.Equal(0, counters.BytesHashed);
        counters.AddHashedBytes(big, 1_000);
        counters.EndHashing(1, big, 1_000);

        var small = counters.BeginHashing(2, @"\M1\a.txt", 10);
        counters.AddHashedBytes(small, 4); // failed halfway
        counters.EndHashing(2, small, 10);

        Assert.Equal((2L, 1_010L), (counters.FilesHashed, counters.BytesHashed)); // as before: each file counts its size
    }

    [Fact]
    public void Now_is_the_file_that_has_been_hashing_longest_with_its_progress()
    {
        var time = new FakeTimeProvider();
        var counters = new ScanCounters();
        counters.AddFiles(3, 500_000_000_000 + 20);
        var big = counters.BeginHashing(1, @"\AppData\Local\Docker\wsl\disk\docker_data.vhdx", 500_000_000_000);
        counters.AddHashedBytes(big, 125_000_000_000);
        var small = counters.BeginHashing(2, @"\AppData\semver.js", 10); // started later by another thread
        counters.EndHashing(2, small, 10);
        counters.BeginHashing(3, @"\AppData\b.js", 10);

        var snapshot = new ScanProgressTracker(time).Sample(1, "AppData", ScanPhase.Hashing, false, counters);

        Assert.Equal(@"\AppData\Local\Docker\wsl\disk\docker_data.vhdx", snapshot.CurrentPath);
        Assert.Equal(25, snapshot.CurrentFilePercent);
        Assert.Equal(500_000_000_000, snapshot.CurrentFileSize);

        counters.EndHashing(1, big, 500_000_000_000);
        Assert.Equal(@"\AppData\b.js", counters.CurrentPath);
    }

    [Fact]
    public void Cancelled_files_do_not_count()
    {
        var counters = new ScanCounters();
        var file = counters.BeginHashing(1, @"\M1\big.bin", 1_000);
        counters.AddHashedBytes(file, 600);

        counters.AbandonHashing(1, file);

        Assert.Equal((0L, 0L), (counters.FilesHashed, counters.BytesHashed));
        Assert.Null(counters.LongestHashing());
    }

    [Fact]
    public void The_hasher_reports_the_bytes_it_reads()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("big.bin");
        File.WriteAllBytes(path, new byte[(3 * Sha1FileHasher.BufferSize) + 5]);
        var reported = new List<long>();

        var result = new Sha1FileHasher().Hash(path, TestContext.Current.CancellationToken, reported.Add);

        Assert.Equal(4, reported.Count); // per 1 MB buffer, not only at the end
        Assert.Equal(result.BytesRead, reported.Sum());
    }
}

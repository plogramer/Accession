using Accession.Core.Scanning;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Scanning;

public class ScanProgressTrackerTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly ScanCounters _counters = new();

    private ScanProgressSnapshot Sample(ScanProgressTracker tracker, bool paused = false) =>
        tracker.Sample(1, "M1", ScanPhase.Hashing, paused, _counters);

    [Fact]
    public void Counters_are_thread_safe()
    {
        Parallel.For(0, 10_000, _ =>
        {
            _counters.AddFiles(1, 10);
            _counters.AddHashed(10);
            _counters.AddFolders(1);
            _counters.AddError();
        });

        Assert.Equal(10_000, _counters.FilesFound);
        Assert.Equal(100_000, _counters.BytesFound);
        Assert.Equal(10_000, _counters.FilesHashed);
        Assert.Equal(100_000, _counters.BytesHashed);
        Assert.Equal(10_000, _counters.FoldersFound);
        Assert.Equal(10_000, _counters.Errors);
    }

    [Fact]
    public void Throughput_is_measured_over_the_window()
    {
        var tracker = new ScanProgressTracker(_time);
        _counters.AddFiles(1000, 1_000_000_000);
        Sample(tracker);

        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            for (var f = 0; f < 10; f++)
            {
                _counters.AddHashed(10_000_000); // 100 MB/s, 10 files/s
            }
        }

        var snapshot = Sample(tracker);
        Assert.Equal(10, snapshot.FilesPerSecond, precision: 3);
        Assert.Equal(100_000_000, snapshot.BytesPerSecond, precision: 0);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.Elapsed);
    }

    [Fact]
    public void Eta_is_only_given_after_enumeration()
    {
        var tracker = new ScanProgressTracker(_time);
        _counters.AddFiles(10, 1000);
        Sample(tracker);
        _time.Advance(TimeSpan.FromSeconds(1));
        _counters.AddHashed(100);

        Assert.Null(Sample(tracker).Eta);

        _counters.EnumerationDone = true;
        _time.Advance(TimeSpan.FromSeconds(1));
        _counters.AddHashed(100);

        // 200 bytes in 2 s = 100 B/s; 800 bytes remaining -> 8 s.
        Assert.Equal(TimeSpan.FromSeconds(8), Sample(tracker).Eta);
    }

    [Fact]
    public void Paused_scan_reports_zero_rate_and_no_eta()
    {
        var tracker = new ScanProgressTracker(_time);
        _counters.AddFiles(10, 1000);
        _counters.EnumerationDone = true;
        Sample(tracker);
        _time.Advance(TimeSpan.FromSeconds(1));
        _counters.AddHashed(100);

        var snapshot = Sample(tracker, paused: true);

        Assert.Equal(0, snapshot.BytesPerSecond);
        Assert.Null(snapshot.Eta);
        Assert.True(snapshot.IsPaused);
    }

    [Fact]
    public void Old_samples_leave_the_window()
    {
        var tracker = new ScanProgressTracker(_time, TimeSpan.FromSeconds(10));
        _counters.AddFiles(1, 1_000_000);
        Sample(tracker);
        _time.Advance(TimeSpan.FromSeconds(1));
        _counters.AddHashed(900_000); // fast burst at the start
        Sample(tracker);

        for (var i = 0; i < 20; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            Sample(tracker); // then nothing
        }

        Assert.Equal(0, Sample(tracker).BytesPerSecond);
    }

    [Theory]
    [InlineData(0, 0, false, 0)]
    [InlineData(0, 0, true, 100)]
    [InlineData(1000, 250, false, 25)]
    public void Percent_by_bytes(long bytesFound, long bytesHashed, bool enumerationDone, double expected)
    {
        var snapshot = new ScanProgressSnapshot(1, "M1", ScanPhase.Hashing, false, 0, 0, bytesFound, 0, bytesHashed, 0, 0, 0,
            TimeSpan.Zero, null, null, enumerationDone);

        Assert.Equal(expected, snapshot.PercentByBytes);
    }
}

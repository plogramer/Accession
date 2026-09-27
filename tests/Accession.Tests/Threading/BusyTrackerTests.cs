using Accession.Core.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Threading;

public class BusyTrackerTests
{
    private readonly FakeTimeProvider _time = new();

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > timeout)
            {
                throw new TimeoutException("Condition not met.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Quick_work_never_shows_busy()
    {
        var tracker = new BusyTracker(_time);
        var changes = new List<string?>();
        tracker.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        var result = await tracker.RunAsync(_ => Task.FromResult(42), "Loading");

        Assert.Equal(42, result);
        Assert.False(tracker.IsBusy);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Slow_work_shows_busy_after_delay_and_clears_after()
    {
        var tracker = new BusyTracker(_time);
        var release = new TaskCompletionSource();

        var run = tracker.RunAsync(_ => release.Task, "Loading inventory");
        _time.Advance(TimeSpan.FromMilliseconds(299));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(tracker.IsBusy);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntil(() => tracker.IsBusy);
        Assert.Equal("Loading inventory", tracker.Message);
        Assert.False(tracker.CanCancel);

        release.SetResult();
        await run;
        Assert.False(tracker.IsBusy);
        Assert.Null(tracker.Message);
    }

    [Fact]
    public async Task Cancel_signals_the_work_token()
    {
        var tracker = new BusyTracker(_time);
        var started = new TaskCompletionSource();

        var run = tracker.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, "Exporting", cancellable: true);

        await started.Task;
        _time.Advance(BusyTracker.DefaultShowDelay);
        await WaitUntil(() => tracker.CanCancel);

        tracker.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task Exceptions_propagate_and_busy_clears()
    {
        var tracker = new BusyTracker(_time);
        var release = new TaskCompletionSource();

        var run = tracker.RunAsync(async _ =>
        {
            await release.Task;
            throw new InvalidOperationException("boom");
        }, "Working");

        _time.Advance(BusyTracker.DefaultShowDelay);
        await WaitUntil(() => tracker.IsBusy);
        release.SetResult();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Equal("boom", ex.Message);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task Work_can_update_its_message_while_visible()
    {
        var tracker = new BusyTracker(_time);
        var release = new TaskCompletionSource();
        Action<string>? report = null;

        var run = tracker.RunAsync(async (_, update) =>
        {
            report = update;
            update("Exporting… 0 %");
            await release.Task;
            return 1;
        }, "Exporting…", cancellable: true);
        await WaitUntil(() => report is not null);
        Assert.False(tracker.IsBusy); // not shown before the delay, updates or not

        _time.Advance(BusyTracker.DefaultShowDelay);
        await WaitUntil(() => tracker.Message == "Exporting… 0 %");
        report!("Exporting… 50 %");
        Assert.Equal("Exporting… 50 %", tracker.Message);

        release.SetResult();
        Assert.Equal(1, await run);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public async Task Nested_operations_show_latest_message_and_restore_previous()
    {
        var tracker = new BusyTracker(_time);
        var releaseOuter = new TaskCompletionSource();
        var releaseInner = new TaskCompletionSource();

        var outer = tracker.RunAsync(_ => releaseOuter.Task, "Outer");
        _time.Advance(BusyTracker.DefaultShowDelay);
        await WaitUntil(() => tracker.Message == "Outer");

        var inner = tracker.RunAsync(_ => releaseInner.Task, "Inner", cancellable: true);
        _time.Advance(BusyTracker.DefaultShowDelay);
        await WaitUntil(() => tracker.Message == "Inner");
        Assert.True(tracker.CanCancel);

        releaseInner.SetResult();
        await inner;
        Assert.True(tracker.IsBusy);
        Assert.Equal("Outer", tracker.Message);
        Assert.False(tracker.CanCancel);

        releaseOuter.SetResult();
        await outer;
        Assert.False(tracker.IsBusy);
    }
}

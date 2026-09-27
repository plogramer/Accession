using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Locking;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class InventoryLockServiceTests : IDisposable
{
    private readonly TestInventory _inventory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => _inventory.Dispose();

    private sealed record User(string UserName, string MachineName) : IUserContext;

    private InventoryLockService CreateSession(string user, string machine, int pid = 100)
    {
        var userContext = new User(user, machine);
        var audit = new AuditService(_inventory.Database, userContext, _time);
        return new InventoryLockService(_inventory.Database, userContext, audit, _time,
            NullLogger<InventoryLockService>.Instance, Guid.NewGuid(), pid);
    }

    private IReadOnlyList<AuditEntry> Audit() =>
        new AuditService(_inventory.Database, new User("x", "y"), _time).Query(new AuditQuery());

    [Fact]
    public void First_session_acquires_and_second_is_refused_with_holder_details()
    {
        using var first = CreateSession(@"CORP\jdoe", "WS-114", pid: 4242);
        using var second = CreateSession(@"CORP\asmith", "WS-203");

        Assert.Equal(LockAcquireStatus.Acquired, first.TryAcquire().Status);
        Assert.True(first.IsHeld);

        var result = second.TryAcquire();
        Assert.Equal(LockAcquireStatus.LockedByOther, result.Status);
        Assert.False(result.IsStale);
        Assert.False(second.IsHeld);
        Assert.Equal(@"CORP\jdoe", result.Holder!.UserName);
        Assert.Equal("WS-114", result.Holder.MachineName);
        Assert.Equal(4242, result.Holder.ProcessId);
        Assert.Equal(first.SessionGuid, result.Holder.SessionGuid);
        Assert.Equal(_time.GetUtcNow(), result.Holder.LockedAtUtc);
        Assert.Contains(Audit(), a => a.Action == AuditAction.LockAcquired && a.UserName == @"CORP\jdoe");
    }

    [Fact]
    public void Same_session_can_reacquire()
    {
        using var session = CreateSession("u", "m");
        session.TryAcquire();

        Assert.Equal(LockAcquireStatus.Acquired, session.TryAcquire().Status);
    }

    [Fact]
    public void Lock_becomes_stale_after_ten_minutes_without_heartbeat()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(second.TryAcquire().IsStale);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(second.TryAcquire().IsStale);
    }

    [Fact]
    public void Heartbeat_keeps_the_lock_fresh()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();
        first.StartHeartbeat();

        for (var i = 0; i < 15; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var result = second.TryAcquire();
        Assert.False(result.IsStale);
        Assert.Equal(_time.GetUtcNow(), result.Holder!.HeartbeatAtUtc);
    }

    [Fact]
    public void Take_over_is_refused_while_lock_is_fresh()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();

        Assert.False(second.TakeOver());
        Assert.Equal(first.SessionGuid, second.ReadHolder()!.SessionGuid);
    }

    [Fact]
    public void Stale_lock_can_be_taken_over_and_is_audited_with_previous_holder()
    {
        using var first = CreateSession(@"CORP\jdoe", "WS-114");
        using var second = CreateSession(@"CORP\asmith", "WS-203");
        first.TryAcquire();
        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.True(second.TakeOver());

        Assert.True(second.IsHeld);
        Assert.Equal(second.SessionGuid, second.ReadHolder()!.SessionGuid);
        var forced = Assert.Single(Audit(), a => a.Action == AuditAction.LockForced);
        Assert.Equal(@"CORP\asmith", forced.UserName);
        Assert.Contains(@"CORP\\jdoe", forced.Details);
        Assert.Contains("WS-114", forced.Details);
    }

    [Fact]
    public void Previous_holder_detects_takeover_on_next_heartbeat()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();
        _time.Advance(TimeSpan.FromMinutes(11));
        second.TakeOver();
        var lost = false;
        first.LockLost += (_, _) => lost = true;

        first.Beat();

        Assert.True(lost);
        Assert.False(first.IsHeld);
        Assert.Equal(second.SessionGuid, second.ReadHolder()!.SessionGuid);
    }

    [Fact]
    public void Release_clears_the_row_and_is_audited()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();

        first.Release();

        Assert.False(first.IsHeld);
        Assert.Null(first.ReadHolder());
        Assert.Contains(Audit(), a => a.Action == AuditAction.LockReleased);
        Assert.Equal(LockAcquireStatus.Acquired, second.TryAcquire().Status);
    }

    [Fact]
    public void Release_after_losing_the_lock_does_not_free_the_new_holder()
    {
        using var first = CreateSession("u1", "m1");
        using var second = CreateSession("u2", "m2");
        first.TryAcquire();
        _time.Advance(TimeSpan.FromMinutes(11));
        second.TakeOver();

        first.Release();

        Assert.Equal(second.SessionGuid, second.ReadHolder()!.SessionGuid);
    }
}

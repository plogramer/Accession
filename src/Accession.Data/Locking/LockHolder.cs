namespace Accession.Data.Locking;

/// <summary>Who currently holds (or last held) the inventory lock.</summary>
public sealed record LockHolder(
    string UserName,
    string MachineName,
    int ProcessId,
    Guid SessionGuid,
    DateTimeOffset LockedAtUtc,
    DateTimeOffset HeartbeatAtUtc);

public enum LockAcquireStatus
{
    Acquired,
    LockedByOther,
}

/// <param name="Holder">The other holder when <see cref="Status"/> is <see cref="LockAcquireStatus.LockedByOther"/>.</param>
/// <param name="IsStale">True when the other holder's heartbeat is older than <see cref="InventoryLockService.StaleAfter"/>.</param>
public sealed record LockAcquireResult(LockAcquireStatus Status, LockHolder? Holder, bool IsStale)
{
    public static readonly LockAcquireResult Acquired = new(LockAcquireStatus.Acquired, null, false);
}

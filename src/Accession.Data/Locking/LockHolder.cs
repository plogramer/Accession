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

    /// <summary>
    /// The lock was left by this user's own earlier session on this computer, which is no longer running (it was not
    /// closed properly): the lock was taken back and <see cref="LockAcquireResult.Holder"/> is the old session.
    /// </summary>
    Recovered,

    LockedByOther,
}

/// <param name="Holder">The other holder (<see cref="LockAcquireStatus.LockedByOther"/>) or the old session (<see cref="LockAcquireStatus.Recovered"/>).</param>
/// <param name="IsStale">True when the other holder's heartbeat is older than <see cref="InventoryLockService.StaleAfter"/>.</param>
/// <param name="IsOtherWindowHere">The holder is this user in another Accession window on this computer.</param>
public sealed record LockAcquireResult(LockAcquireStatus Status, LockHolder? Holder, bool IsStale, bool IsOtherWindowHere = false)
{
    public static readonly LockAcquireResult Acquired = new(LockAcquireStatus.Acquired, null, false);
}

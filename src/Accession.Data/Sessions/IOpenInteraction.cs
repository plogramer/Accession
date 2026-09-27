using Accession.Data.Locking;

namespace Accession.Data.Sessions;

public enum LockConflictChoice
{
    Cancel,
    OpenReadOnly,
    TakeOver,
}

public enum RootUnreachableChoice
{
    Cancel,
    Retry,
    ContinueOffline,
    ChangeRootPath,
}

/// <summary>Who holds the lock and whether it can be taken over.</summary>
/// <param name="IsStale">No heartbeat for more than <see cref="InventoryLockService.StaleAfter"/>: Take Over is allowed.</param>
/// <param name="IsOtherWindowHere">The holder is this user in another Accession window on this computer.</param>
public sealed record LockConflict(LockHolder Holder, bool IsStale, bool IsOtherWindowHere);

/// <summary>User decision when the root folder cannot be reached. <see cref="NewRootPath"/> is set for ChangeRootPath.</summary>
public sealed record RootUnreachableResolution(RootUnreachableChoice Choice, string? NewRootPath = null);

/// <summary>
/// Questions the open workflow asks the user. Implemented by the UI; calls may arrive on a background thread.
/// </summary>
public interface IOpenInteraction
{
    /// <summary>The inventory uses an older schema. Return true to back it up and upgrade.</summary>
    bool ConfirmUpgrade(int fromVersion, int toVersion);

    /// <summary>Someone else holds the lock (or this user in another Accession window). Take over is only offered when stale.</summary>
    LockConflictChoice ResolveLockConflict(LockConflict conflict);

    /// <summary>The root folder does not exist or cannot be reached.</summary>
    RootUnreachableResolution ResolveRootUnreachable(string rootPath);
}

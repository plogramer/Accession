using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Core.Time;
using Accession.Data.Audit;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Locking;

/// <summary>
/// Single-user lock stored in the <c>InventoryLock</c> row (requirements 5.2). The holder refreshes a heartbeat
/// every minute; a lock whose heartbeat is older than 10 minutes is stale and may be taken over.
/// </summary>
public sealed class InventoryLockService : IDisposable
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly InventoryDatabase _database;
    private readonly IUserContext _user;
    private readonly IAuditService _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InventoryLockService> _logger;
    private readonly int _processId;
    private readonly IProcessProbe _processes;
    private readonly Lock _gate = new();
    private ITimer? _heartbeat;

    public InventoryLockService(
        InventoryDatabase database,
        IUserContext user,
        IAuditService audit,
        TimeProvider timeProvider,
        ILogger<InventoryLockService> logger,
        Guid sessionGuid,
        int processId,
        IProcessProbe? processes = null)
    {
        _processes = processes ?? SystemProcessProbe.Instance;
        _database = database;
        _user = user;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
        SessionGuid = sessionGuid;
        _processId = processId;
    }

    /// <summary>Identifies this app session in the lock row.</summary>
    public Guid SessionGuid { get; }

    /// <summary>True after this session acquired the lock and until it is released or lost.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Raised (on a timer thread) when the heartbeat finds that another session has taken the lock.</summary>
    public event EventHandler? LockLost;

    /// <summary>
    /// Acquires the lock if it is free or already ours. A lock left by this user on this computer by a session that is
    /// no longer running (the app was not closed properly) is taken back (<see cref="LockAcquireStatus.Recovered"/>).
    /// Otherwise reports the other holder.
    /// </summary>
    public LockAcquireResult TryAcquire()
    {
        using var scope = _database.Open();
        using var transaction = scope.BeginTransaction();
        var current = ReadRow(scope);
        if (current.IsLocked && current.Holder is { } holder && holder.SessionGuid != SessionGuid)
        {
            var mine = IsThisUserHere(holder);
            if (mine && !_processes.IsAccessionRunning(holder.ProcessId))
            {
                WriteOwnLock(scope);
                _audit.Write(scope, AuditAction.LockRecovered, details: new { SessionGuid, ProcessId = _processId, PreviousHolder = holder });
                transaction.Commit();
                IsHeld = true;
                _logger.LogWarning("Recovered the lock left by an earlier session (process {ProcessId}) that was not closed properly", holder.ProcessId);
                return new LockAcquireResult(LockAcquireStatus.Recovered, holder, IsStale: false);
            }

            return new LockAcquireResult(LockAcquireStatus.LockedByOther, holder, IsStale(holder), IsOtherWindowHere: mine);
        }

        WriteOwnLock(scope);
        _audit.Write(scope, AuditAction.LockAcquired, details: new { SessionGuid, ProcessId = _processId });
        transaction.Commit();
        IsHeld = true;
        return LockAcquireResult.Acquired;
    }

    /// <summary>
    /// Takes over a stale lock held by someone else. Returns false (and changes nothing) if the lock is no longer
    /// stale, e.g. because the holder came back.
    /// </summary>
    public bool TakeOver()
    {
        using var scope = _database.Open();
        using var transaction = scope.BeginTransaction();
        var current = ReadRow(scope);
        var previous = current.IsLocked ? current.Holder : null;
        if (previous is not null && previous.SessionGuid != SessionGuid && !IsStale(previous))
        {
            return false;
        }

        WriteOwnLock(scope);
        _audit.Write(scope, AuditAction.LockForced, details: new
        {
            SessionGuid,
            ProcessId = _processId,
            PreviousHolder = previous,
        });
        transaction.Commit();
        IsHeld = true;
        return true;
    }

    /// <summary>Who holds the lock now, or null if it is free.</summary>
    public LockHolder? ReadHolder()
    {
        using var scope = _database.Open();
        return ReadHolder(scope);
    }

    /// <summary>Who holds the lock of the inventory open in <paramref name="scope"/>, or null if it is free (also works read-only).</summary>
    public static LockHolder? ReadHolder(DbScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var row = ReadRow(scope);
        return row.IsLocked ? row.Holder : null;
    }

    /// <summary>Starts refreshing the heartbeat every <see cref="HeartbeatInterval"/>.</summary>
    public void StartHeartbeat()
    {
        lock (_gate)
        {
            _heartbeat ??= _timeProvider.CreateTimer(_ => Beat(), null, HeartbeatInterval, HeartbeatInterval);
        }
    }

    /// <summary>Stops the heartbeat and frees the lock if this session still holds it.</summary>
    public void Release()
    {
        StopHeartbeat();
        if (!IsHeld)
        {
            return;
        }

        IsHeld = false;
        using var scope = _database.Open();
        using var transaction = scope.BeginTransaction();
        var released = scope.Connection.Execute(
            """
            UPDATE InventoryLock SET IsLocked = 0, LockedBy = NULL, MachineName = NULL, ProcessId = NULL,
                SessionGuid = NULL, LockedAtUtc = NULL, HeartbeatAtUtc = NULL
            WHERE LockId = 1 AND SessionGuid = @SessionGuid
            """,
            new { SessionGuid },
            scope.Transaction);
        if (released > 0)
        {
            _audit.Write(scope, AuditAction.LockReleased, details: new { SessionGuid });
        }

        transaction.Commit();
    }

    public void Dispose() => StopHeartbeat();

    /// <summary>One heartbeat; internal so tests can call it directly.</summary>
    internal void Beat()
    {
        if (!IsHeld)
        {
            return;
        }

        try
        {
            using var scope = _database.Open();
            LongReads.WriteStarting(scope.Connection);
            var updated = scope.Connection.Execute(
                "UPDATE InventoryLock SET HeartbeatAtUtc = @now WHERE LockId = 1 AND IsLocked = 1 AND SessionGuid = @SessionGuid",
                new { now = _timeProvider.GetUtcNow(), SessionGuid });
            if (updated == 0)
            {
                _logger.LogWarning("Inventory lock was taken over by another session");
                IsHeld = false;
                StopHeartbeat();
                LockLost?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            // Transient (e.g. network blip): try again on the next tick.
            _logger.LogWarning(ex, "Inventory lock heartbeat failed");
        }
    }

    private void StopHeartbeat()
    {
        lock (_gate)
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
        }
    }

    private bool IsThisUserHere(LockHolder holder) =>
        string.Equals(holder.UserName, _user.UserName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(holder.MachineName, _user.MachineName, StringComparison.OrdinalIgnoreCase);

    private bool IsStale(LockHolder holder) => _timeProvider.GetUtcNow() - holder.HeartbeatAtUtc > StaleAfter;

    private void WriteOwnLock(DbScope scope)
    {
        var now = _timeProvider.GetUtcNow();
        scope.Connection.Execute(
            """
            UPDATE InventoryLock SET IsLocked = 1, LockedBy = @LockedBy, MachineName = @MachineName, ProcessId = @ProcessId,
                SessionGuid = @SessionGuid, LockedAtUtc = @now, HeartbeatAtUtc = @now
            WHERE LockId = 1
            """,
            new { LockedBy = _user.UserName, _user.MachineName, ProcessId = _processId, SessionGuid, now },
            scope.Transaction);
    }

    private static (bool IsLocked, LockHolder? Holder) ReadRow(DbScope scope)
    {
        var row = scope.Connection.QuerySingle<LockRow>("SELECT * FROM InventoryLock WHERE LockId = 1", transaction: scope.Transaction);
        if (row.IsLocked == 0 || row.SessionGuid is null)
        {
            return (false, null);
        }

        var lockedAt = UtcTimestamp.TryParse(row.LockedAtUtc, out var l) ? l : DateTimeOffset.MinValue;
        var heartbeat = UtcTimestamp.TryParse(row.HeartbeatAtUtc, out var h) ? h : lockedAt;
        return (true, new LockHolder(
            row.LockedBy ?? string.Empty,
            row.MachineName ?? string.Empty,
            (int)(row.ProcessId ?? 0),
            Guid.TryParse(row.SessionGuid, out var guid) ? guid : Guid.Empty,
            lockedAt,
            heartbeat));
    }

    private sealed class LockRow
    {
        public long IsLocked { get; set; }
        public string? LockedBy { get; set; }
        public string? MachineName { get; set; }
        public long? ProcessId { get; set; }
        public string? SessionGuid { get; set; }
        public string? LockedAtUtc { get; set; }
        public string? HeartbeatAtUtc { get; set; }
    }
}

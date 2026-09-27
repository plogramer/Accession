using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Locking;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Sessions;

/// <summary>Builds the per-inventory objects (database, audit, lock) for a session.</summary>
public sealed class InventorySessionFactory
{
    private readonly IUserContext _user;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IProcessProbe _processes;

    public InventorySessionFactory(IUserContext user, TimeProvider timeProvider, ILoggerFactory loggerFactory, IProcessProbe? processes = null)
    {
        _processes = processes ?? SystemProcessProbe.Instance;
        _user = user;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    public IUserContext User => _user;

    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>A writable database with a lock service that has not acquired the lock yet.</summary>
    internal (InventoryDatabase Database, AuditService Audit, InventoryLockService Lock) CreateWritableParts(string path)
    {
        var database = new InventoryDatabase(path);
        var audit = new AuditService(database, _user, _timeProvider);
        var lockService = new InventoryLockService(database, _user, audit, _timeProvider,
            _loggerFactory.CreateLogger<InventoryLockService>(), Guid.NewGuid(), Environment.ProcessId, _processes);
        return (database, audit, lockService);
    }

    /// <summary>Session for a database whose lock is already held by <paramref name="lockService"/>. Starts the heartbeat.</summary>
    internal InventorySession CreateLocked(InventoryDatabase database, AuditService audit, InventoryLockService lockService)
    {
        var session = new InventorySession(database, audit, lockService, _user.UserName);
        lockService.StartHeartbeat();
        return session;
    }

    internal InventorySession CreateReadOnly(string path)
    {
        var database = new InventoryDatabase(path, isReadOnly: true);
        return new InventorySession(database, new AuditService(database, _user, _timeProvider), lockService: null, _user.UserName);
    }
}

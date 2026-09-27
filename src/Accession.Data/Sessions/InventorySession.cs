using Accession.Core.Model;
using Accession.Data.Audit;
using Accession.Data.Locking;
using Accession.Data.Repositories;

namespace Accession.Data.Sessions;

/// <summary>An inventory the user has open: database, audit, lock and current configuration.</summary>
public sealed class InventorySession : IDisposable
{
    private readonly Lock _gate = new();
    private bool _closed;

    internal InventorySession(InventoryDatabase database, IAuditService audit, InventoryLockService? lockService, string userName)
    {
        UserName = userName;
        Database = database;
        Audit = audit;
        LockService = lockService;
        IsReadOnly = lockService is null || database.IsReadOnly;
        using var scope = database.Open();
        Config = new InventoryConfigRepository(scope).Get();

        if (lockService is not null)
        {
            lockService.LockLost += (_, _) => BecomeReadOnly();
        }
    }

    public InventoryDatabase Database { get; }

    /// <summary><c>DOMAIN\user</c> of the person using this session.</summary>
    public string UserName { get; }

    public string DbPath => Database.Path;

    public IAuditService Audit { get; }

    /// <summary>The lock this session holds; null for read-only sessions.</summary>
    public InventoryLockService? LockService { get; }

    /// <summary>True when opened read-only or after the lock was taken over by someone else.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>False when the root folder could not be reached at open ("offline"): scanning is disabled.</summary>
    public bool IsRootAvailable { get; set; } = true;

    /// <summary>Snapshot of the <c>InventoryConfig</c> row; call <see cref="ReloadConfig"/> after changes.</summary>
    public InventoryConfig Config { get; private set; }

    /// <summary>Raised when the session switches to read-only because the lock was lost (may be on a timer thread).</summary>
    public event EventHandler? BecameReadOnly;

    public void ReloadConfig()
    {
        using var scope = Database.Open();
        Config = new InventoryConfigRepository(scope).Get();
    }

    /// <summary>Throws if the session may not modify the inventory.</summary>
    public void EnsureWritable()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("The inventory is open read-only.");
        }
    }

    /// <summary>Audits the close and releases the lock. Safe to call more than once.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
        }

        if (LockService is { IsHeld: true })
        {
            Audit.Write(AuditAction.InventoryClosed);
            LockService.Release();
        }

        LockService?.Dispose();
    }

    public void Dispose() => Close();

    private void BecomeReadOnly()
    {
        IsReadOnly = true;
        BecameReadOnly?.Invoke(this, EventArgs.Empty);
    }
}

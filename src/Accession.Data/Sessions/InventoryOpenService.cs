using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Locking;
using Accession.Data.Migrations;
using Accession.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Sessions;

/// <summary>The inventory is in use and must be upgraded, so it cannot be opened read-only.</summary>
public sealed class InventoryInUseException(LockHolder holder)
    : InvalidOperationException(
        $"This inventory must be upgraded to the current version, but it is open by {holder.UserName} on {holder.MachineName}. " +
        "Try again when they have closed it.")
{
    public LockHolder Holder { get; } = holder;
}

/// <summary>
/// Opens an existing inventory (requirements INV-05, INV-06, INV-11, 5.2, 5.3): schema check and upgrade,
/// lock acquisition or read-only fallback, last-opened update, audit, and root folder check.
/// </summary>
public sealed class InventoryOpenService
{
    private readonly InventorySessionFactory _factory;
    private readonly IAppInfo _appInfo;
    private readonly ILogger<InventoryOpenService> _logger;
    private readonly RootPathService _rootPaths;

    public InventoryOpenService(InventorySessionFactory factory, IAppInfo appInfo, RootPathService rootPaths, ILogger<InventoryOpenService> logger)
    {
        _factory = factory;
        _appInfo = appInfo;
        _rootPaths = rootPaths;
        _logger = logger;
    }

    /// <summary>Opens <paramref name="path"/>. Returns null if the user cancelled along the way.</summary>
    /// <exception cref="NotAnInventoryException">The file is not an Accession inventory.</exception>
    /// <exception cref="SchemaTooNewException">The inventory was created by a newer version.</exception>
    /// <exception cref="InventoryInUseException">An upgrade is needed but someone else has the inventory open.</exception>
    public InventorySession? Open(string path, IOpenInteraction interaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(interaction);
        path = Path.GetFullPath(path);

        var inspection = SchemaInspector.Inspect(path);
        switch (inspection.State)
        {
            case SchemaState.NotAnInventory:
                throw new NotAnInventoryException(path);
            case SchemaState.Newer:
                throw new SchemaTooNewException(inspection.FileVersion!.Value, inspection.AppVersion);
        }

        var needsUpgrade = inspection.State == SchemaState.Older;
        if (needsUpgrade && !interaction.ConfirmUpgrade(inspection.FileVersion!.Value, inspection.AppVersion))
        {
            return null;
        }

        var session = OpenWithLock(path, needsUpgrade, interaction);
        if (session is null)
        {
            return null;
        }

        return EnsureRootAvailable(session, interaction) ? session : null;
    }

    private InventorySession? OpenWithLock(string path, bool needsUpgrade, IOpenInteraction interaction)
    {
        var (database, audit, lockService) = _factory.CreateWritableParts(path);
        var acquired = false;
        while (!acquired)
        {
            var result = lockService.TryAcquire();
            if (result.Status == LockAcquireStatus.Acquired)
            {
                acquired = true;
                continue;
            }

            switch (interaction.ResolveLockConflict(result.Holder!, result.IsStale))
            {
                case LockConflictChoice.Cancel:
                    lockService.Dispose();
                    return null;

                case LockConflictChoice.OpenReadOnly:
                    lockService.Dispose();
                    if (needsUpgrade)
                    {
                        throw new InventoryInUseException(result.Holder!);
                    }

                    return OpenReadOnly(path);

                case LockConflictChoice.TakeOver when result.IsStale:
                    // False if the holder came back meanwhile: the loop asks again with fresh details.
                    acquired = lockService.TakeOver();
                    break;
            }
        }

        try
        {
            if (needsUpgrade)
            {
                var upgrade = new MigrationRunner(_factory.TimeProvider).Upgrade(path, _factory.User.UserName, _appInfo.Version);
                audit.Write(AuditAction.SchemaUpgraded, details: upgrade);
            }

            using (var scope = database.Open())
            using (var transaction = scope.BeginTransaction())
            {
                var recovered = Scanning.ScanRecovery.Recover(scope, _factory.TimeProvider.GetUtcNow());
                if (recovered > 0)
                {
                    _logger.LogWarning("{Count} media had an unfinished scan and were marked Incomplete", recovered);
                }

                new InventoryConfigRepository(scope).UpdateLastOpened(_factory.TimeProvider.GetUtcNow(), _factory.User.UserName, path);
                audit.Write(scope, AuditAction.InventoryOpened, details: new { AppVersion = _appInfo.Version });
                transaction.Commit();
            }

            return _factory.CreateLocked(database, audit, lockService);
        }
        catch
        {
            lockService.Release();
            lockService.Dispose();
            throw;
        }
    }

    private InventorySession OpenReadOnly(string path)
    {
        var session = _factory.CreateReadOnly(path);

        // LCK-05: the only write a read-only session makes; skipped if the file itself is not writable.
        try
        {
            new AuditService(new InventoryDatabase(path), _factory.User, _factory.TimeProvider)
                .Write(AuditAction.InventoryOpenedReadOnly, details: new { AppVersion = _appInfo.Version });
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation(ex, "Could not write the read-only open audit entry for {Path}", path);
        }

        return session;
    }

    private bool EnsureRootAvailable(InventorySession session, IOpenInteraction interaction)
    {
        while (!Directory.Exists(session.Config.RootPath))
        {
            var resolution = interaction.ResolveRootUnreachable(session.Config.RootPath);
            switch (resolution.Choice)
            {
                case RootUnreachableChoice.Retry:
                    continue;

                case RootUnreachableChoice.ContinueOffline:
                    session.IsRootAvailable = false;
                    return true;

                case RootUnreachableChoice.ChangeRootPath when resolution.NewRootPath is not null && !session.IsReadOnly:
                    _rootPaths.Apply(session, resolution.NewRootPath);
                    continue;

                case RootUnreachableChoice.ChangeRootPath:
                    continue;

                default:
                    session.Close();
                    return false;
            }
        }

        session.IsRootAvailable = true;
        return true;
    }
}

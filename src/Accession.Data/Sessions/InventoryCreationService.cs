using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Locking;
using Accession.Data.Schema;

namespace Accession.Data.Sessions;

/// <summary>Creates a new inventory and opens it (requirements INV-01 … INV-04).</summary>
public sealed class InventoryCreationService
{
    private readonly InventorySessionFactory _factory;
    private readonly IAppInfo _appInfo;

    public InventoryCreationService(InventorySessionFactory factory, IAppInfo appInfo)
    {
        _factory = factory;
        _appInfo = appInfo;
    }

    /// <summary>Validates, creates the file, acquires the lock and returns the open session.</summary>
    /// <exception cref="InventoryValidationException">The request is not valid.</exception>
    public InventorySession Create(CreateInventoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = InventoryValidation.ValidateCreate(request);
        if (errors.Count > 0)
        {
            throw new InventoryValidationException(errors);
        }

        var now = _factory.TimeProvider.GetUtcNow();
        var path = Path.GetFullPath(request.SavePath);
        var config = new InventoryConfig
        {
            InventoryGuid = Guid.NewGuid(),
            RootPath = PathRules.NormalizeDirectory(request.RootFolder),
            ClientName = request.ClientName.Trim(),
            ClientCode = request.ClientCode.Trim(),
            MatterName = request.MatterName.Trim(),
            MatterCode = request.MatterCode.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            MatterUrl = string.IsNullOrWhiteSpace(request.MatterUrl) ? null : request.MatterUrl.Trim(),
            CreatedAtUtc = now,
            CreatedBy = _factory.User.UserName,
            CreatedOnMachine = _factory.User.MachineName,
            CreatedAppVersion = _appInfo.Version,
            LastOpenedAtUtc = now,
            LastOpenedBy = _factory.User.UserName,
        };

        DatabaseCreator.Create(path, config);

        var (database, audit, lockService) = _factory.CreateWritableParts(path);
        audit.Write(AuditAction.InventoryCreated, details: new
        {
            config.InventoryGuid,
            config.ClientName,
            config.ClientCode,
            config.MatterName,
            config.MatterCode,
            config.RootPath,
            DbPath = path,
            AppVersion = _appInfo.Version,
        });

        if (lockService.TryAcquire().Status != LockAcquireStatus.Acquired)
        {
            // Only possible if someone opened the brand-new file within milliseconds.
            throw new InvalidOperationException("The new inventory was opened by someone else before it could be locked.");
        }

        return _factory.CreateLocked(database, audit, lockService);
    }
}

using System.Globalization;
using Accession.Core.Time;
using Accession.Data.Schema;
using Dapper;

namespace Accession.Data.Migrations;

public sealed record UpgradeResult(int FromVersion, int ToVersion, string BackupPath);

/// <summary>Upgrades older inventories to the current schema, after taking a backup copy (requirement INV-11).</summary>
public sealed class MigrationRunner
{
    private readonly IReadOnlyList<IMigration> _migrations;
    private readonly int _appVersion;
    private readonly TimeProvider _timeProvider;

    /// <summary>Runner with this application's migrations.</summary>
    public MigrationRunner(TimeProvider timeProvider)
        : this(KnownMigrations.All, InventorySchema.CurrentVersion, timeProvider)
    {
    }

    internal MigrationRunner(IReadOnlyList<IMigration> migrations, int appVersion, TimeProvider timeProvider)
    {
        _migrations = migrations.OrderBy(m => m.ToVersion).ToList();
        _appVersion = appVersion;
        _timeProvider = timeProvider;

        var expected = 2;
        foreach (var migration in _migrations)
        {
            if (migration.ToVersion != expected++)
            {
                throw new InvalidOperationException("Migrations must cover consecutive versions starting at 2.");
            }
        }

        if (_migrations.Count > 0 && _migrations[^1].ToVersion != appVersion)
        {
            throw new InvalidOperationException($"The last migration must target version {appVersion}.");
        }
    }

    public SchemaInspection Inspect(string path) => SchemaInspector.Inspect(path, _appVersion);

    /// <summary>
    /// Backs up the file to <c>&lt;name&gt;.v&lt;old&gt;.&lt;yyyyMMddHHmmss&gt;.bak</c>, then applies all pending
    /// migrations in one transaction. On failure nothing is changed and the backup remains.
    /// </summary>
    public UpgradeResult Upgrade(string path, string userName, string appVersionText)
    {
        var inspection = Inspect(path);
        switch (inspection.State)
        {
            case SchemaState.NotAnInventory:
                throw new NotAnInventoryException(path);
            case SchemaState.Newer:
                throw new SchemaTooNewException(inspection.FileVersion!.Value, _appVersion);
            case SchemaState.Current:
                throw new InvalidOperationException("The inventory is already at the current schema version.");
        }

        var fromVersion = inspection.FileVersion!.Value;
        var now = _timeProvider.GetUtcNow();
        var backupPath = $"{path}.v{fromVersion}.{now.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.bak";
        File.Copy(path, backupPath, overwrite: false);

        using var connection = SqliteConnectionFactory.Open(path);
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var migration in _migrations.Where(m => m.ToVersion > fromVersion))
        {
            migration.Apply(connection, transaction);
            connection.Execute(
                "INSERT INTO SchemaMigration (Version, AppliedAtUtc, AppliedBy, AppVersion) VALUES (@Version, @At, @By, @App)",
                new { Version = migration.ToVersion, At = UtcTimestamp.ToText(now), By = userName, App = appVersionText },
                transaction);
        }

        connection.Execute(
            "UPDATE InventoryConfig SET SchemaVersion = @Version WHERE ConfigId = 1",
            new { Version = _appVersion },
            transaction);
        transaction.Commit();

        return new UpgradeResult(fromVersion, _appVersion, backupPath);
    }
}

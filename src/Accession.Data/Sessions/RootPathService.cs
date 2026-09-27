using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Data.Repositories;

namespace Accession.Data.Sessions;

/// <summary>Result of checking a candidate root folder before relocating (INV-09).</summary>
/// <param name="MediaNotFound">Registered media whose folder does not exist under the new root.</param>
public sealed record RootPathPreview(string NewRootPath, bool Exists, IReadOnlyList<string> MediaNotFound);

/// <summary>Changes the inventory root folder after the inventory and media were moved (requirement INV-09).</summary>
public sealed class RootPathService
{
    /// <summary>Checks the new root and lists media that would not be found under it. Changes nothing.</summary>
    public RootPathPreview Preview(InventorySession session, string newRootPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRootPath);

        var normalized = PathRules.NormalizeDirectory(newRootPath);
        if (!Directory.Exists(normalized))
        {
            return new RootPathPreview(normalized, false, []);
        }

        using var scope = session.Database.Open();
        var missing = new MediaRepository(scope).ListActive()
            .Where(m => !Directory.Exists(Path.Combine(normalized, m.MediaId)))
            .Select(m => m.MediaId)
            .ToList();
        return new RootPathPreview(normalized, true, missing);
    }

    /// <summary>Updates <c>InventoryConfig.RootPath</c> and audits <c>RootPathChanged</c> with old/new path.</summary>
    public RootPathPreview Apply(InventorySession session, string newRootPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EnsureWritable();

        var preview = Preview(session, newRootPath);
        if (!preview.Exists)
        {
            throw new DirectoryNotFoundException($"The folder '{preview.NewRootPath}' does not exist or cannot be reached.");
        }

        var oldPath = session.Config.RootPath;
        using (var scope = session.Database.Open())
        using (var transaction = scope.BeginTransaction())
        {
            new InventoryConfigRepository(scope).UpdateRootPath(preview.NewRootPath);
            session.Audit.Write(scope, AuditAction.RootPathChanged, details: new
            {
                OldPath = oldPath,
                NewPath = preview.NewRootPath,
                preview.MediaNotFound,
            });
            transaction.Commit();
        }

        session.ReloadConfig();
        session.IsRootAvailable = true;
        return preview;
    }
}

using Accession.Core.Model;
using Accession.Data.Repositories;
using Accession.Data.Sessions;

namespace Accession.Data.MediaManagement;

/// <summary>A folder under the root that is not registered as media.</summary>
public sealed record DiscoveredFolder(string MediaId, string FullPath, bool PreviouslyDeleted);

/// <summary>Outcome of comparing the root's subfolders with registered media (requirements 5.3).</summary>
public sealed record DiscoveryResult(
    bool RootAvailable,
    IReadOnlyList<DiscoveredFolder> NewFolders,
    IReadOnlyList<Media> MissingMedia,
    IReadOnlyList<Media> NeverScanned,
    IReadOnlyList<Media> Incomplete)
{
    public static readonly DiscoveryResult RootUnavailable = new(false, [], [], [], []);

    public bool HasFindings => NewFolders.Count > 0 || MissingMedia.Count > 0 || NeverScanned.Count > 0 || Incomplete.Count > 0;
}

/// <summary>Finds new, missing, never-scanned and incomplete media (DSC-01 … DSC-05).</summary>
public sealed class MediaDiscoveryService
{
    /// <summary>
    /// Lists the root's immediate subfolders and compares them with active media (case-insensitive).
    /// In a writable session, media whose folder disappeared are marked Missing and restored when it reappears.
    /// </summary>
    public DiscoveryResult Discover(InventorySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var root = session.Config.RootPath;

        IReadOnlyList<string> folderNames;
        try
        {
            if (!Directory.Exists(root))
            {
                return DiscoveryResult.RootUnavailable;
            }

            folderNames = MediaFolders.ListCandidateNames(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiscoveryResult.RootUnavailable;
        }

        var onDisk = folderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var scope = session.Database.Open();
        var media = new MediaRepository(scope);
        var active = media.ListActive();

        if (!session.IsReadOnly)
        {
            using var transaction = scope.BeginTransaction();
            foreach (var item in active)
            {
                var exists = onDisk.Contains(item.MediaId);
                if (!exists && item.Status != MediaStatus.Missing)
                {
                    media.MarkMissing(item.MediaKey);
                }
                else if (exists && item.Status == MediaStatus.Missing)
                {
                    media.RestoreFromMissing(item.MediaKey);
                }
            }

            transaction.Commit();
            active = media.ListActive();
        }

        var activeIds = active.Select(m => m.MediaId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletedIds = media.ListDeletedMediaIds();
        var newFolders = folderNames
            .Where(name => !activeIds.Contains(name))
            .Select(name => new DiscoveredFolder(name, MediaFolders.FullPath(root, name), deletedIds.Contains(name)))
            .ToList();

        // In a read-only session statuses can't be updated, so derive "missing" from the disk check directly.
        var missing = active.Where(m => m.Status == MediaStatus.Missing || !onDisk.Contains(m.MediaId)).ToList();
        return new DiscoveryResult(
            true,
            newFolders,
            missing,
            active.Where(m => m.Status == MediaStatus.New && onDisk.Contains(m.MediaId)).ToList(),
            active.Where(m => m.Status == MediaStatus.Incomplete && onDisk.Contains(m.MediaId)).ToList());
    }
}

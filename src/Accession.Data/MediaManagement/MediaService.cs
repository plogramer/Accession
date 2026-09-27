using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Data.Repositories;
using Accession.Data.Sessions;

namespace Accession.Data.MediaManagement;

public sealed record RejectedFolder(string Path, string Reason);

public sealed record AddMediaResult(IReadOnlyList<Media> Added, IReadOnlyList<RejectedFolder> Rejected);

/// <summary>Adds and deletes media (requirements 5.4).</summary>
public sealed class MediaService
{
    /// <summary>Statuses during which a media cannot be deleted (MED-05).</summary>
    public static readonly IReadOnlySet<MediaStatus> BusyStatuses =
        new HashSet<MediaStatus> { MediaStatus.Queued, MediaStatus.Scanning, MediaStatus.Hashing, MediaStatus.Paused };

    private readonly TimeProvider _timeProvider;

    public MediaService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Registers folders as media. Each must be an immediate child of the root (MED-02) and not already registered
    /// (MED-03). Valid folders are added in one transaction with a <c>MediaAdded</c> audit entry each.
    /// </summary>
    public AddMediaResult Add(InventorySession session, IEnumerable<string> folderPaths)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(folderPaths);
        session.EnsureWritable();

        var root = PathRules.NormalizeDirectory(session.Config.RootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The root folder '{root}' cannot be reached.");
        }

        var namesOnDisk = MediaFolders.ListCandidateNames(root);
        var rejected = new List<RejectedFolder>();
        var accepted = new List<string>();
        foreach (var path in folderPaths)
        {
            var (mediaId, reason) = Check(path, root, namesOnDisk, accepted);
            if (mediaId is null)
            {
                rejected.Add(new RejectedFolder(path, reason!));
            }
            else
            {
                accepted.Add(mediaId);
            }
        }

        var added = new List<Media>();
        if (accepted.Count == 0)
        {
            return new AddMediaResult(added, rejected);
        }

        using var scope = session.Database.Open();
        using var transaction = scope.BeginTransaction();
        var media = new MediaRepository(scope);
        var deletedIds = media.ListDeletedMediaIds();
        var now = _timeProvider.GetUtcNow();
        foreach (var mediaId in accepted)
        {
            if (media.FindActive(mediaId) is not null)
            {
                rejected.Add(new RejectedFolder(MediaFolders.FullPath(root, mediaId), $"'{mediaId}' is already in this inventory."));
                continue;
            }

            var relativePath = MediaFolders.RelativePath(mediaId);
            var key = media.Insert(mediaId, relativePath, now, session.UserName);
            session.Audit.Write(scope, AuditAction.MediaAdded, mediaId, new
            {
                RelativePath = relativePath,
                FullPath = MediaFolders.FullPath(root, mediaId),
                PreviouslyDeleted = deletedIds.Contains(mediaId),
            });
            added.Add(media.Get(key)!);
        }

        transaction.Commit();
        return new AddMediaResult(added, rejected);
    }

    /// <summary>
    /// Deletes a media's folders, files, errors and summaries and marks it deleted. Files on disk are not touched;
    /// scan history and audit entries are kept (MED-05).
    /// </summary>
    /// <returns>The counts that were removed.</returns>
    public ScanTotals Delete(InventorySession session, long mediaKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.EnsureWritable();

        using var scope = session.Database.Open();
        using var transaction = scope.BeginTransaction();
        var media = new MediaRepository(scope);
        var item = media.Get(mediaKey);
        if (item is null || item.IsDeleted)
        {
            throw new InvalidOperationException("The media does not exist or was already deleted.");
        }

        if (BusyStatuses.Contains(item.Status))
        {
            throw new InvalidOperationException($"'{item.MediaId}' is {item.Status.ToString().ToLowerInvariant()}. Cancel the scan before deleting it.");
        }

        var scanData = new ScanDataRepository(scope);
        var totals = scanData.ComputeTotals(mediaKey);
        scanData.DeleteForMedia(mediaKey);
        media.SoftDelete(mediaKey, _timeProvider.GetUtcNow(), session.UserName);
        session.Audit.Write(scope, AuditAction.MediaDeleted, item.MediaId, new
        {
            item.RelativePath,
            totals.FolderCount,
            totals.FileCount,
            totals.TotalBytes,
            totals.ErrorCount,
        });
        transaction.Commit();
        return totals;
    }

    private static (string? MediaId, string? Reason) Check(string path, string root, IReadOnlyList<string> namesOnDisk, List<string> accepted)
    {
        string full;
        try
        {
            full = PathRules.NormalizeDirectory(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "The path is not valid.");
        }

        var parent = Path.GetDirectoryName(full);
        if (parent is null || !PathRules.AreSameDirectory(parent, root))
        {
            return (null, $"Media folders must be directly inside the root folder '{root}'.");
        }

        var name = Path.GetFileName(full);
        var onDisk = namesOnDisk.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (MediaFolders.IgnoredAtRoot.Contains(name))
        {
            return (null, $"'{name}' is a system folder and cannot be added as media.");
        }

        if (onDisk is null)
        {
            return (null, "The folder does not exist.");
        }

        if (accepted.Contains(onDisk, StringComparer.OrdinalIgnoreCase))
        {
            return (null, $"'{onDisk}' was selected more than once.");
        }

        return (onDisk, null);
    }
}

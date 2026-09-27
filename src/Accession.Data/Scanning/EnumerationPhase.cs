using System.Threading.Channels;
using Accession.Core.Model;
using Accession.Core.Scanning;

namespace Accession.Data.Scanning;

/// <summary>
/// Scan phase 1 (SCN-20): lists folders in parallel, records folders and files through the writer and feeds
/// files to the hashing phase. Reparse points are recorded but never followed (SCN-16); folders that cannot be
/// listed are logged and the scan continues (SCN-52).
/// </summary>
public sealed class EnumerationPhase
{
    private readonly IDirectoryLister _lister;

    public EnumerationPhase(IDirectoryLister lister)
    {
        _lister = lister;
    }

    /// <summary>Lists <paramref name="startFolders"/> and everything below them.</summary>
    public async Task RunAsync(
        ScanContext context,
        IReadOnlyCollection<FolderWork> startFolders,
        int threads,
        ChannelWriter<HashWork> hashQueue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (startFolders.Count == 0)
        {
            return;
        }

        var folders = Channel.CreateUnbounded<FolderWork>();
        var pending = (long)startFolders.Count;
        foreach (var folder in startFolders)
        {
            folders.Writer.TryWrite(folder);
        }

        async Task Worker()
        {
            await foreach (var folder in folders.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await context.Pause.WaitAsync(cancellationToken).ConfigureAwait(false);
                    var children = await ProcessAsync(context, folder, hashQueue, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref pending, children.Count);
                    foreach (var child in children)
                    {
                        folders.Writer.TryWrite(child);
                    }
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0)
                    {
                        folders.Writer.TryComplete();
                    }
                }
            }
        }

        var workers = Enumerable.Range(0, Math.Max(1, threads)).Select(_ => Task.Run(Worker, cancellationToken)).ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        finally
        {
            folders.Writer.TryComplete();
        }
    }

    private async Task<IReadOnlyList<FolderWork>> ProcessAsync(
        ScanContext context, FolderWork folder, ChannelWriter<HashWork> hashQueue, CancellationToken cancellationToken)
    {
        context.Counters.CurrentPath = folder.RelativePath;
        IReadOnlyList<DirectoryEntry> entries;
        try
        {
            entries = await context.Retry.RunAsync(() => _lister.List(context.FullPath(folder.RelativePath)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The folder row exists (from its parent's listing); record the error and mark it processed.
            await context.Writer.WriteAsync(context.Error(folder.RelativePath, ScanItemType.Folder, ex), cancellationToken).ConfigureAwait(false);
            await context.Writer.WriteAsync(new FolderListingCommand(folder.FolderId, [], []), cancellationToken).ConfigureAwait(false);
            context.Counters.AddError();
            return [];
        }

        var childFolders = new List<FolderRow>();
        var childWork = new List<FolderWork>();
        var files = new List<FileRow>();
        var toHash = new List<HashWork>();
        var skippedFileIds = new List<long>();
        var errors = new List<ErrorCommand>();
        long bytes = 0;

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                var id = context.Ids.NextFolderId();
                var path = ScanPaths.ChildFolder(folder.RelativePath, entry.Name);
                childFolders.Add(new FolderRow(id, context.MediaKey, folder.FolderId, entry.Name, path,
                    entry.CreatedUtc, entry.ModifiedUtc, entry.AccessedUtc, entry.IsReparsePoint, IsEnumerated: entry.IsReparsePoint));
                if (entry.IsReparsePoint)
                {
                    errors.Add(ReparseSkipped(context, path, ScanItemType.Folder));
                }
                else
                {
                    childWork.Add(new FolderWork(id, path));
                }
            }
            else
            {
                var id = context.Ids.NextFileId();
                var path = ScanPaths.File(folder.RelativePath, entry.Name);
                files.Add(new FileRow(id, context.MediaKey, folder.FolderId, entry.Name, ScanPaths.Extension(entry.Name), entry.Size,
                    entry.CreatedUtc, entry.ModifiedUtc, entry.AccessedUtc));
                bytes += entry.Size;
                if (entry.IsReparsePoint)
                {
                    // A file link would be hashed through to its target, possibly outside the media: record only.
                    errors.Add(ReparseSkipped(context, path, ScanItemType.File));
                    skippedFileIds.Add(id);
                }
                else
                {
                    toHash.Add(new HashWork(id, path, entry.Size, entry.ModifiedUtc));
                }
            }
        }

        await context.Writer.WriteAsync(new FolderListingCommand(folder.FolderId, childFolders, files), cancellationToken).ConfigureAwait(false);
        foreach (var skippedId in skippedFileIds)
        {
            await context.Writer.WriteAsync(new HashResultCommand(skippedId, null, HashStatus.Skipped, null), cancellationToken).ConfigureAwait(false);
        }

        foreach (var error in errors)
        {
            await context.Writer.WriteAsync(error, cancellationToken).ConfigureAwait(false);
        }

        context.Counters.AddFolders(childFolders.Count);
        context.Counters.AddFiles(files.Count, bytes);
        foreach (var item in toHash)
        {
            await hashQueue.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }

        return childWork;
    }

    private static ErrorCommand ReparseSkipped(ScanContext context, string path, ScanItemType itemType) =>
        context.Error(path, itemType, ScanErrorType.ReparsePointSkipped, ScanErrorSeverity.Info, null,
            "Reparse point (junction or symbolic link) recorded but not followed.");
}

using System.Threading.Channels;
using Accession.Core.Model;
using Accession.Core.Scanning;

namespace Accession.Data.Scanning;

/// <summary>
/// Scan phase 2 (SCN-21): hashes queued files with a worker pool. Locked or unreadable files get
/// <c>HashStatus = Error</c> plus an error row; files that changed while being scanned get a
/// <c>ChangedDuringScan</c> warning but keep their hash (SCN-43).
/// </summary>
public sealed class HashingPhase
{
    private readonly IFileHasher _hasher;

    public HashingPhase(IFileHasher hasher)
    {
        _hasher = hasher;
    }

    public async Task RunAsync(ScanContext context, ChannelReader<HashWork> queue, int threads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        async Task Worker()
        {
            await foreach (var item in queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await context.Pause.WaitAsync(cancellationToken).ConfigureAwait(false);
                await HashOneAsync(context, item, cancellationToken).ConfigureAwait(false);
            }
        }

        var workers = Enumerable.Range(0, Math.Max(1, threads)).Select(_ => Task.Run(Worker, cancellationToken));
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task HashOneAsync(ScanContext context, HashWork item, CancellationToken cancellationToken)
    {
        var counters = context.Counters;
        var file = counters.BeginHashing(item.FileId, item.RelativePath, item.Size);
        try
        {
            var result = await context.Retry.RunAsync(() =>
                {
                    counters.RestartHashing(file); // a retry reads the file again from the start
                    return _hasher.Hash(context.FullPath(item.RelativePath), cancellationToken, read => counters.AddHashedBytes(file, read));
                }, cancellationToken)
                .ConfigureAwait(false);
            await context.Writer.WriteAsync(
                new HashResultCommand(item.FileId, result.Sha1, HashStatus.Hashed, context.TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);

            if (result.SizeAfter != item.Size || (item.ModifiedUtc is { } modified && result.ModifiedAfterUtc != modified))
            {
                await context.Writer.WriteAsync(context.Error(item.RelativePath, ScanItemType.File, ScanErrorType.ChangedDuringScan,
                    ScanErrorSeverity.Warning, null,
                    $"The file changed while it was scanned (size {item.Size:N0} → {result.SizeAfter:N0} bytes, " +
                    $"modified {item.ModifiedUtc:u} → {result.ModifiedAfterUtc:u}). The hash is of the content read."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            counters.AbandonHashing(item.FileId, file);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await context.Writer.WriteAsync(
                new HashResultCommand(item.FileId, null, HashStatus.Error, context.TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            await context.Writer.WriteAsync(context.Error(item.RelativePath, ScanItemType.File, ex), cancellationToken).ConfigureAwait(false);
            context.Counters.AddError();
        }

        counters.EndHashing(item.FileId, file, item.Size);
    }
}

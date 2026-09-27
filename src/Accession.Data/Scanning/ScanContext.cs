using Accession.Core.Model;
using Accession.Core.Scanning;

namespace Accession.Data.Scanning;

/// <summary>A folder waiting to be listed.</summary>
public sealed record FolderWork(long FolderId, string RelativePath);

/// <summary>A file waiting to be hashed, with the size and modified time captured at enumeration (SCN-41).</summary>
public sealed record HashWork(long FileId, string RelativePath, long Size, DateTimeOffset? ModifiedUtc);

/// <summary>Everything the scan phases share for one media scan run.</summary>
public sealed class ScanContext
{
    public required long MediaKey { get; init; }
    public required string MediaId { get; init; }
    public required long ScanId { get; init; }
    public required string RootPath { get; init; }
    public required ScanDbWriter Writer { get; init; }
    public required ScanIdAllocator Ids { get; init; }
    public required ScanCounters Counters { get; init; }
    public required PauseGate Pause { get; init; }
    public required NetworkRetry Retry { get; init; }
    public required TimeProvider TimeProvider { get; init; }

    public string FullPath(string relativePath) => ScanPaths.ToFullPath(RootPath, relativePath);

    public ErrorCommand Error(string relativePath, ScanItemType itemType, ScanErrorType type, ScanErrorSeverity severity, int? code, string message) =>
        new(new ScanErrorEntry
        {
            MediaKey = MediaKey,
            ScanId = ScanId,
            RelativePath = relativePath,
            ItemType = itemType,
            ErrorType = type,
            Severity = severity,
            ErrorCode = code,
            Message = message,
            OccurredAtUtc = TimeProvider.GetUtcNow(),
        });

    public ErrorCommand Error(string relativePath, ScanItemType itemType, Exception exception)
    {
        var info = ScanErrorClassifier.Classify(exception);
        return Error(relativePath, itemType, info.Type, info.Severity, info.ErrorCode, info.Message);
    }
}

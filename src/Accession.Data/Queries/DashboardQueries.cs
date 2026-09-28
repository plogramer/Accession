using System.Text.Json;
using Accession.Core.Model;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Queries;

/// <summary>Media filter for dashboard queries: null = all active media.</summary>
public sealed record DashboardFilter(IReadOnlyCollection<long>? MediaKeys)
{
    public static readonly DashboardFilter All = new((IReadOnlyCollection<long>?)null);

    /// <summary>Value for <c>@MediaKeysJson</c>.</summary>
    public string? MediaKeysJson => MediaKeys is null ? null : JsonSerializer.Serialize(MediaKeys);
}

public sealed class DashboardSummary
{
    public long MediaCount { get; init; }
    public long FolderCount { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
    public long HashedCount { get; init; }
    public long ErrorCount { get; init; }
}

public sealed class DashboardMediaRow
{
    public long MediaKey { get; init; }
    public string MediaId { get; init; } = string.Empty;
    public MediaStatus Status { get; init; }
    public long FolderCount { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
    public long HashedCount { get; init; }
    public long ErrorCount { get; init; }
    public int ScanCount { get; init; }
    public DateTimeOffset? LastScanCompletedUtc { get; init; }
}

public sealed class CategoryTotal
{
    public int CategoryId { get; init; }
    public string Category { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class ExtensionTotal
{
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class DuplicateSummary
{
    public long HashedFiles { get; init; }
    public long UniqueFiles { get; init; }
    public long HashedBytes { get; init; }
    public long UniqueBytes { get; init; }
    public long NotHashedFiles { get; init; }

    public long DuplicateFiles => HashedFiles - UniqueFiles;

    public long DuplicateBytes => HashedBytes - UniqueBytes;
}

public sealed class MediaDuplicates
{
    public long MediaKey { get; init; }
    public long HashedFiles { get; init; }
    public long UniqueFiles { get; init; }

    public long DuplicateFiles => HashedFiles - UniqueFiles;
}

public sealed class YearTotal
{
    public string Year { get; init; } = string.Empty;
    public long FileCount { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class LargeFile
{
    public long MediaKey { get; init; }
    public string MediaId { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? ModifiedUtc { get; init; }
}

/// <summary>Typed access to the "Dashboard" query set (requirements 5.7).</summary>
public sealed class DashboardQueries
{
    public const string SetName = "Dashboard";
    public const int LargestFilesLimit = 100;

    public DashboardQueries(string? overrideDirectory, ILogger<DashboardQueries> logger)
    {
        Runner = new QuerySetRunner(SetName, overrideDirectory, logger);
    }

    public QuerySetRunner Runner { get; }

    public DashboardSummary Summary(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<DashboardSummary>(database, "SummaryTiles", filter, cancellationToken).Single();

    public IReadOnlyList<DashboardMediaRow> ByMedia(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<DashboardMediaRow>(database, "ByMedia", filter, cancellationToken);

    public IReadOnlyList<CategoryTotal> ByCategory(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<CategoryTotal>(database, "ByCategory", filter, cancellationToken);

    public IReadOnlyList<ExtensionTotal> ByExtension(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<ExtensionTotal>(database, "ByExtension", filter, cancellationToken);

    // Duplicates, DuplicatesByMedia and ByYear read every file: long reads that give way to writes (LongReads) and stop at
    // once when cancelled. They throw a SqliteException with LongReads.InterruptedErrorCode when interrupted.

    public DuplicateSummary Duplicates(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<DuplicateSummary>(database, "Duplicates", filter, cancellationToken, longRead: true).Single();

    public IReadOnlyList<MediaDuplicates> DuplicatesByMedia(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<MediaDuplicates>(database, "DuplicatesByMedia", filter, cancellationToken, longRead: true);

    public IReadOnlyList<YearTotal> ByYear(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<YearTotal>(database, "ByYear", filter, cancellationToken, longRead: true);

    public IReadOnlyList<LargeFile> LargestFiles(InventoryDatabase database, DashboardFilter filter, CancellationToken cancellationToken = default) =>
        Run<LargeFile>(database, "LargestFiles", filter, cancellationToken, LargestFilesLimit);

    private IReadOnlyList<T> Run<T>(InventoryDatabase database, string name, DashboardFilter filter, CancellationToken cancellationToken,
        int limit = 0, bool longRead = false)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = database.Open();
        using var lease = longRead ? LongReads.Enter(database.Path, scope.Connection) : null;
        // Cancelling (a new media selection) stops the query itself, not only the wait for it.
        using var cancel = longRead ? cancellationToken.Register(() => LongReads.Interrupt(database.Path)) : default;
        return Runner.Query<T>(scope, name, new { filter.MediaKeysJson, Limit = limit }, cancellationToken);
    }
}

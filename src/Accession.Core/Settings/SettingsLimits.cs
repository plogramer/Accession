namespace Accession.Core.Settings;

/// <summary>Defaults and allowed ranges for user settings (requirements section 5.11).</summary>
public static class SettingsLimits
{
    public const int DefaultEnumerationThreads = 4;
    public const int MinEnumerationThreads = 1;
    public const int MaxEnumerationThreads = 16;

    public const int DefaultHashingThreads = 4;
    public const int MinHashingThreads = 1;
    public const int MaxHashingThreads = 32;

    public const int DefaultDbBatchSize = 10_000;
    public const int MinDbBatchSize = 1_000;
    public const int MaxDbBatchSize = 100_000;

    public const int MaxRecentInventories = 15;

    /// <summary>Rows-per-page choices on the web Files screen; the first is the default.</summary>
    public static readonly IReadOnlyList<int> FilePageSizes = [1_000, 2_000, 5_000, 10_000, 50_000];

    /// <summary>Optional Files columns shown by default.</summary>
    public static readonly IReadOnlyList<string> DefaultFilesColumns = ["extension", "folder", "media", "size", "modified", "category", "hash", "copies"];
}

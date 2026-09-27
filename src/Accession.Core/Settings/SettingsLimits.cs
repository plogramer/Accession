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
}

using System.Text.Json;

namespace Accession.Core.Settings;

/// <summary>Per-Windows-user application settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    // Display
    public SizeUnitSystem SizeUnit { get; set; } = SizeUnitSystem.Decimal;
    public DisplayTimeZone DisplayTimeZone { get; set; } = DisplayTimeZone.Local;

    // Scanning
    public int EnumerationThreads { get; set; } = SettingsLimits.DefaultEnumerationThreads;
    public int HashingThreads { get; set; } = SettingsLimits.DefaultHashingThreads;
    public int DbBatchSize { get; set; } = SettingsLimits.DefaultDbBatchSize;

    // Export
    /// <summary>Default export folder; empty means the user's Documents folder.</summary>
    public string DefaultExportFolder { get; set; } = string.Empty;
    public bool SplitExportPerMedia { get; set; }

    // UI state
    public List<RecentInventory> RecentInventories { get; set; } = [];
    public Dictionary<string, WindowPlacement> Windows { get; set; } = [];
    public Dictionary<string, List<GridColumnLayout>> GridLayouts { get; set; } = [];

    /// <summary>Export folder to use: the configured one if it exists, otherwise Documents.</summary>
    public string ResolveExportFolder()
    {
        if (!string.IsNullOrWhiteSpace(DefaultExportFolder) && Directory.Exists(DefaultExportFolder))
        {
            return DefaultExportFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>Deep copy.</summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsJson.Options);
        return JsonSerializer.Deserialize<AppSettings>(json, SettingsJson.Options)!;
    }

    /// <summary>Resets display, scanning and export options to defaults. UI state (recent list, layouts) is kept.</summary>
    public void RestoreDefaultOptions()
    {
        var defaults = new AppSettings();
        SizeUnit = defaults.SizeUnit;
        DisplayTimeZone = defaults.DisplayTimeZone;
        EnumerationThreads = defaults.EnumerationThreads;
        HashingThreads = defaults.HashingThreads;
        DbBatchSize = defaults.DbBatchSize;
        DefaultExportFolder = defaults.DefaultExportFolder;
        SplitExportPerMedia = defaults.SplitExportPerMedia;
    }

    /// <summary>Clamps values to their allowed ranges and repairs missing or invalid entries.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(SizeUnit))
        {
            SizeUnit = SizeUnitSystem.Decimal;
        }

        if (!Enum.IsDefined(DisplayTimeZone))
        {
            DisplayTimeZone = DisplayTimeZone.Local;
        }

        EnumerationThreads = Math.Clamp(EnumerationThreads, SettingsLimits.MinEnumerationThreads, SettingsLimits.MaxEnumerationThreads);
        HashingThreads = Math.Clamp(HashingThreads, SettingsLimits.MinHashingThreads, SettingsLimits.MaxHashingThreads);
        DbBatchSize = Math.Clamp(DbBatchSize, SettingsLimits.MinDbBatchSize, SettingsLimits.MaxDbBatchSize);
        DefaultExportFolder = DefaultExportFolder?.Trim() ?? string.Empty;

        RecentInventories = (RecentInventories ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Path))
            .DistinctBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Take(SettingsLimits.MaxRecentInventories)
            .ToList();

        Windows = new Dictionary<string, WindowPlacement>(
            (Windows ?? []).Where(w => w.Value is not null), StringComparer.Ordinal);
        GridLayouts = new Dictionary<string, List<GridColumnLayout>>(
            (GridLayouts ?? []).Where(g => g.Value is not null), StringComparer.Ordinal);
    }
}

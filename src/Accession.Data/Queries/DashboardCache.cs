using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Queries;

/// <summary>The Dashboard sections that read every file (duplicates, duplicates per media, files by year).</summary>
public sealed record SlowDashboard(DuplicateSummary Duplicates, IReadOnlyList<MediaDuplicates> PerMedia, IReadOnlyList<YearTotal> Years);

/// <summary>
/// Keeps the slow Dashboard sections on this computer (one small JSON file per inventory in the user's local app data),
/// so opening a large inventory does not recalculate them from every file each time. An entry is used only while the
/// inventory's data is unchanged: <see cref="DataVersion"/> changes when a scan, rescan, retry or media deletion changes
/// the files, and when the dashboard queries change. Each media selection has its own entry.
/// </summary>
public sealed class DashboardCache
{
    /// <summary>Bump when <see cref="SlowDashboard"/> or its meaning changes: older files are ignored.</summary>
    public const int FormatVersion = 1;

    /// <summary>Media selections kept per inventory (the least recently saved go first).</summary>
    public const int MaxEntries = 50;

    private static readonly Lock Gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string? _directory;
    private readonly ILogger<DashboardCache> _logger;

    /// <param name="directory">Where the cache files go; null disables the cache.</param>
    public DashboardCache(string? directory, ILogger<DashboardCache> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    /// <summary>
    /// A fingerprint of everything the slow sections depend on, read from the small Media and ScanLog tables (no pass over
    /// the files): each media's status, scan count, file, hash and byte totals, the latest scan, and the query texts.
    /// </summary>
    public static string DataVersion(InventoryDatabase database, DashboardQueries queries)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(queries);
        using var scope = database.Open();
        var text = new StringBuilder();
        foreach (var row in scope.Connection.Query(
                     """
                     SELECT MediaKey, Status, IsDeleted, ScanCount, LastScanCompletedUtc, FileCount, HashedCount, TotalBytes, ErrorCount
                     FROM Media ORDER BY MediaKey
                     """))
        {
            var fields = (IDictionary<string, object?>)row;
            text.AppendJoin('|', fields.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))).Append('\n');
        }

        var scans = scope.Connection.QuerySingle("SELECT COUNT(*) AS Scans, MAX(ScanId) AS LastScan, MAX(EndedAtUtc) AS LastEnded FROM ScanLog");
        text.AppendJoin('|', ((IDictionary<string, object?>)scans).Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))).Append('\n');
        foreach (var name in new[] { "Duplicates", "DuplicatesByMedia", "ByYear" })
        {
            text.Append(queries.Runner.LoadSql(name)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The saved sections for this inventory, data version and media selection; null when there are none.</summary>
    public SlowDashboard? Get(Guid inventory, string dataVersion, DashboardFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (Load(inventory) is not { } file || file.FormatVersion != FormatVersion || file.DataVersion != dataVersion)
        {
            return null;
        }

        return file.Entries.FirstOrDefault(e => e.Filter == Key(filter))?.Value;
    }

    /// <summary>Saves the sections. Entries of an older data version are dropped.</summary>
    public void Put(Guid inventory, string dataVersion, DashboardFilter filter, SlowDashboard value)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(value);
        if (_directory is null)
        {
            return;
        }

        lock (Gate)
        {
            var file = Load(inventory);
            var entries = file is not null && file.FormatVersion == FormatVersion && file.DataVersion == dataVersion ? file.Entries : [];
            var key = Key(filter);
            entries = [.. entries.Where(e => e.Filter != key).TakeLast(MaxEntries - 1), new CacheEntry(key, value)];
            Save(inventory, new CacheFile(FormatVersion, dataVersion, entries));
        }
    }

    /// <summary>"all" or the selected media keys in order, e.g. "1,4,9".</summary>
    internal static string Key(DashboardFilter filter) =>
        filter.MediaKeys is null ? "all" : string.Join(',', filter.MediaKeys.Order().Select(k => k.ToString(CultureInfo.InvariantCulture)));

    private string? PathOf(Guid inventory) => _directory is null ? null : Path.Combine(_directory, inventory.ToString("N") + ".json");

    private CacheFile? Load(Guid inventory)
    {
        if (PathOf(inventory) is not { } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            lock (Gate)
            {
                return JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Ignoring the dashboard cache {Path}", path);
            return null;
        }
    }

    private void Save(Guid inventory, CacheFile file)
    {
        var path = PathOf(inventory)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(file, Json));
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the dashboard cache {Path}", path);
        }
    }

    private sealed record CacheFile(int FormatVersion, string DataVersion, IReadOnlyList<CacheEntry> Entries);

    private sealed record CacheEntry(string Filter, SlowDashboard Value);
}

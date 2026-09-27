using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Accession.Core.Settings;

/// <summary>Stores <see cref="AppSettings"/> in a JSON file (normally <c>%APPDATA%\Accession\settings.json</c>).</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private AppSettings _current;

    public JsonSettingsService(string filePath, ILogger<JsonSettingsService> logger, TimeProvider timeProvider)
    {
        _filePath = filePath;
        _logger = logger;
        _timeProvider = timeProvider;
        _current = Load();
    }

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current.Clone();
            }
        }
    }

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        AppSettings snapshot;
        lock (_gate)
        {
            var updated = _current.Clone();
            change(updated);
            updated.Normalize();
            Save(updated);
            _current = updated;
            snapshot = updated.Clone();
        }

        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(snapshot));
    }

    public void AddRecentInventory(string path, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Update(settings =>
        {
            settings.RecentInventories.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            settings.RecentInventories.Insert(0, new RecentInventory
            {
                Path = path,
                DisplayName = displayName,
                LastOpenedUtc = _timeProvider.GetUtcNow(),
            });
        });
    }

    public void RemoveRecentInventory(string path)
    {
        Update(settings =>
            settings.RecentInventories.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)));
    }

    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Settings file {Path} not found; using defaults", _filePath);
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsJson.Options)
                ?? throw new JsonException("Settings file is empty.");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Settings file {Path} could not be read; using defaults", _filePath);
            BackUpUnreadableFile();
            return new AppSettings();
        }
    }

    private void BackUpUnreadableFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not back up unreadable settings file {Path}", _filePath);
        }
    }

    private void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file and swap it in so a crash never leaves a half-written settings file.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SettingsJson.Options));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}

namespace Accession.Core.Settings;

public interface ISettingsService
{
    /// <summary>A snapshot copy of the current settings. Changing it has no effect; use <see cref="Update"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings were changed and saved.</summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>Applies <paramref name="change"/> to a copy of the settings, normalizes, saves, then raises <see cref="SettingsChanged"/>.</summary>
    void Update(Action<AppSettings> change);

    /// <summary>Adds or moves an inventory to the top of the recent list.</summary>
    void AddRecentInventory(string path, string displayName);

    void RemoveRecentInventory(string path);
}

public sealed class SettingsChangedEventArgs(AppSettings settings) : EventArgs
{
    /// <summary>Snapshot of the settings after the change.</summary>
    public AppSettings Settings { get; } = settings;
}

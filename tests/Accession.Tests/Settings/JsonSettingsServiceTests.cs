using Accession.Core.Settings;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Settings;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly ListLogger<JsonSettingsService> _logger = new();

    private string SettingsPath => _temp.Combine("Accession", "settings.json");

    public void Dispose() => _temp.Dispose();

    private JsonSettingsService CreateService() => new(SettingsPath, _logger, _time);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = CreateService().Current;

        Assert.Equal(SizeUnitSystem.Decimal, settings.SizeUnit);
        Assert.Equal(DisplayTimeZone.Local, settings.DisplayTimeZone);
        Assert.Equal(4, settings.EnumerationThreads);
        Assert.Equal(4, settings.HashingThreads);
        Assert.Equal(10_000, settings.DbBatchSize);
        Assert.Equal(string.Empty, settings.DefaultExportFolder);
        Assert.False(settings.SplitExportPerMedia);
        Assert.Empty(settings.RecentInventories);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Update_saves_and_round_trips()
    {
        var service = CreateService();
        service.Update(s =>
        {
            s.SizeUnit = SizeUnitSystem.Binary;
            s.DisplayTimeZone = DisplayTimeZone.Utc;
            s.EnumerationThreads = 8;
            s.HashingThreads = 12;
            s.DbBatchSize = 20_000;
            s.DefaultExportFolder = @"C:\Exports";
            s.SplitExportPerMedia = true;
            s.Windows["Main"] = new WindowPlacement { Left = 10, Top = 20, Width = 1200, Height = 800, IsMaximized = true };
            s.GridLayouts["Media"] = [new GridColumnLayout { Key = "MediaId", Width = 150, DisplayIndex = 0 }];
        });

        var reloaded = CreateService().Current;

        Assert.Equal(SizeUnitSystem.Binary, reloaded.SizeUnit);
        Assert.Equal(DisplayTimeZone.Utc, reloaded.DisplayTimeZone);
        Assert.Equal(8, reloaded.EnumerationThreads);
        Assert.Equal(12, reloaded.HashingThreads);
        Assert.Equal(20_000, reloaded.DbBatchSize);
        Assert.Equal(@"C:\Exports", reloaded.DefaultExportFolder);
        Assert.True(reloaded.SplitExportPerMedia);
        Assert.True(reloaded.Windows["Main"].IsMaximized);
        Assert.Equal(1200, reloaded.Windows["Main"].Width);
        Assert.Equal("MediaId", Assert.Single(reloaded.GridLayouts["Media"]).Key);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Enums_are_stored_as_text()
    {
        CreateService().Update(s => s.SizeUnit = SizeUnitSystem.Binary);

        Assert.Contains("\"Binary\"", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1_000)]
    [InlineData(-5, -5, -5, 1, 1, 1_000)]
    [InlineData(100, 100, 1_000_000, 16, 32, 100_000)]
    [InlineData(6, 20, 50_000, 6, 20, 50_000)]
    public void Values_are_clamped_to_allowed_ranges(int enumThreads, int hashThreads, int batch, int expectedEnum, int expectedHash, int expectedBatch)
    {
        var service = CreateService();
        service.Update(s =>
        {
            s.EnumerationThreads = enumThreads;
            s.HashingThreads = hashThreads;
            s.DbBatchSize = batch;
        });

        var settings = service.Current;
        Assert.Equal(expectedEnum, settings.EnumerationThreads);
        Assert.Equal(expectedHash, settings.HashingThreads);
        Assert.Equal(expectedBatch, settings.DbBatchSize);
    }

    [Fact]
    public void Out_of_range_values_in_file_are_clamped_on_load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "hashingThreads": 500, "enumerationThreads": 0, "sizeUnit": 7 }""");

        var settings = CreateService().Current;

        Assert.Equal(32, settings.HashingThreads);
        Assert.Equal(1, settings.EnumerationThreads);
        Assert.Equal(SizeUnitSystem.Decimal, settings.SizeUnit);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "sizeUnit": "Furlongs" }""")]
    public void Corrupt_file_gives_defaults_logs_warning_and_keeps_a_backup(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);

        var settings = CreateService().Current;

        Assert.Equal(SettingsLimits.DefaultHashingThreads, settings.HashingThreads);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(content, File.ReadAllText(SettingsPath + ".corrupt"));
    }

    [Fact]
    public void Current_is_a_snapshot()
    {
        var service = CreateService();

        service.Current.HashingThreads = 30;

        Assert.Equal(SettingsLimits.DefaultHashingThreads, service.Current.HashingThreads);
    }

    [Fact]
    public void SettingsChanged_is_raised_with_new_values()
    {
        var service = CreateService();
        AppSettings? received = null;
        service.SettingsChanged += (_, e) => received = e.Settings;

        service.Update(s => s.SizeUnit = SizeUnitSystem.Binary);

        Assert.NotNull(received);
        Assert.Equal(SizeUnitSystem.Binary, received.SizeUnit);
    }

    [Fact]
    public void Recent_inventory_is_added_to_top_with_timestamp()
    {
        var service = CreateService();
        service.AddRecentInventory(@"\\nas\a.sqlite", "A");
        _time.Advance(TimeSpan.FromMinutes(5));
        service.AddRecentInventory(@"\\nas\b.sqlite", "B");

        var recent = service.Current.RecentInventories;

        Assert.Equal([@"\\nas\b.sqlite", @"\\nas\a.sqlite"], recent.Select(r => r.Path));
        Assert.Equal(_time.GetUtcNow(), recent[0].LastOpenedUtc);
    }

    [Fact]
    public void Reopening_moves_entry_to_top_without_duplicates_ignoring_case()
    {
        var service = CreateService();
        service.AddRecentInventory(@"\\nas\a.sqlite", "A");
        service.AddRecentInventory(@"\\nas\b.sqlite", "B");
        service.AddRecentInventory(@"\\NAS\A.SQLITE", "A renamed");

        var recent = service.Current.RecentInventories;

        Assert.Equal(2, recent.Count);
        Assert.Equal("A renamed", recent[0].DisplayName);
    }

    [Fact]
    public void Recent_list_keeps_at_most_15_entries()
    {
        var service = CreateService();
        for (var i = 1; i <= 20; i++)
        {
            service.AddRecentInventory($@"C:\inv\{i}.sqlite", $"Inventory {i}");
        }

        var recent = service.Current.RecentInventories;

        Assert.Equal(15, recent.Count);
        Assert.Equal(@"C:\inv\20.sqlite", recent[0].Path);
        Assert.Equal(@"C:\inv\6.sqlite", recent[^1].Path);
    }

    [Fact]
    public void Recent_inventory_can_be_removed()
    {
        var service = CreateService();
        service.AddRecentInventory(@"C:\inv\a.sqlite", "A");
        service.AddRecentInventory(@"C:\inv\b.sqlite", "B");

        service.RemoveRecentInventory(@"C:\INV\A.sqlite");

        Assert.Equal(@"C:\inv\b.sqlite", Assert.Single(service.Current.RecentInventories).Path);
    }

    [Fact]
    public void RestoreDefaultOptions_keeps_recent_list()
    {
        var service = CreateService();
        service.AddRecentInventory(@"C:\inv\a.sqlite", "A");
        service.Update(s => s.HashingThreads = 16);

        service.Update(s => s.RestoreDefaultOptions());

        var settings = service.Current;
        Assert.Equal(SettingsLimits.DefaultHashingThreads, settings.HashingThreads);
        Assert.Single(settings.RecentInventories);
    }

    [Fact]
    public void ResolveExportFolder_falls_back_to_documents_when_missing()
    {
        var settings = new AppSettings { DefaultExportFolder = _temp.Combine("does-not-exist") };

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), settings.ResolveExportFolder());

        settings.DefaultExportFolder = _temp.Path;
        Assert.Equal(_temp.Path, settings.ResolveExportFolder());
    }
}

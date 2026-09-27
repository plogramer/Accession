using Accession.Core.Model;
using Accession.Data.MediaManagement;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;

namespace Accession.Tests.MediaManagement;

public sealed class MediaDiscoveryServiceTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly MediaDiscoveryService _discovery = new();
    private readonly MediaService _media;

    public MediaDiscoveryServiceTests()
    {
        _media = new MediaService(_test.Time);
    }

    public void Dispose() => _test.Dispose();

    private long Add(string name) => _media.Add(_test.Session, [Path.Combine(_test.Root, name)]).Added[0].MediaKey;

    private Media Get(long key)
    {
        using var scope = _test.Session.Database.Open();
        return new MediaRepository(scope).Get(key)!;
    }

    private void SetStatus(long key, MediaStatus status)
    {
        using var scope = _test.Session.Database.Open();
        new MediaRepository(scope).SetStatus(key, status);
    }

    [Fact]
    public void New_folders_are_listed_sorted_and_system_folders_ignored()
    {
        _test.CreateFolders("B", "a", "$RECYCLE.BIN", "System Volume Information");
        File.WriteAllText(Path.Combine(_test.Root, "notes.txt"), "files are not media");

        var result = _discovery.Discover(_test.Session);

        Assert.True(result.RootAvailable);
        Assert.Equal(["a", "B"], result.NewFolders.Select(f => f.MediaId));
        Assert.Equal(Path.Combine(_test.Root, "a"), result.NewFolders[0].FullPath);
        Assert.All(result.NewFolders, f => Assert.False(f.PreviouslyDeleted));
    }

    [Fact]
    public void Registered_folders_are_not_new_and_never_scanned_are_reported()
    {
        _test.CreateFolders("M1", "M2");
        var m1 = Add("M1");

        var result = _discovery.Discover(_test.Session);

        Assert.Equal(["M2"], result.NewFolders.Select(f => f.MediaId));
        Assert.Equal([m1], result.NeverScanned.Select(m => m.MediaKey));
        Assert.Empty(result.MissingMedia);
    }

    [Fact]
    public void Missing_folder_marks_media_missing_and_reappearing_restores_status()
    {
        _test.CreateFolders("M1");
        var key = Add("M1");
        SetStatus(key, MediaStatus.CompletedWithErrors);

        Directory.Delete(Path.Combine(_test.Root, "M1"));
        var missing = _discovery.Discover(_test.Session);
        Assert.Equal(MediaStatus.Missing, Get(key).Status);
        Assert.Equal([key], missing.MissingMedia.Select(m => m.MediaKey));

        _test.CreateFolders("M1");
        var back = _discovery.Discover(_test.Session);
        Assert.Equal(MediaStatus.CompletedWithErrors, Get(key).Status);
        Assert.Empty(back.MissingMedia);
    }

    [Fact]
    public void Incomplete_media_are_reported()
    {
        _test.CreateFolders("M1");
        var key = Add("M1");
        SetStatus(key, MediaStatus.Incomplete);

        Assert.Equal([key], _discovery.Discover(_test.Session).Incomplete.Select(m => m.MediaKey));
    }

    [Fact]
    public void Deleted_media_folder_is_offered_as_previously_deleted()
    {
        _test.CreateFolders("M1");
        _media.Delete(_test.Session, Add("M1"));

        var folder = Assert.Single(_discovery.Discover(_test.Session).NewFolders);

        Assert.Equal("M1", folder.MediaId);
        Assert.True(folder.PreviouslyDeleted);
    }

    [Fact]
    public void Case_only_difference_is_the_same_media()
    {
        _test.CreateFolders("Custodian");
        Add("Custodian");

        Assert.Empty(_discovery.Discover(_test.Session).NewFolders);
    }

    [Fact]
    public void Unreachable_root_is_reported()
    {
        Directory.Delete(_test.Root, recursive: true);

        var result = _discovery.Discover(_test.Session);

        Assert.False(result.RootAvailable);
        Assert.False(result.HasFindings);
    }
}

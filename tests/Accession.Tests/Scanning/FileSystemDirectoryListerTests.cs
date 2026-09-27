using Accession.Core.Scanning;
using Accession.Tests.TestSupport;

namespace Accession.Tests.Scanning;

public sealed class FileSystemDirectoryListerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileSystemDirectoryLister _lister = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Lists_files_and_folders_with_metadata_in_utc()
    {
        var dir = _temp.Combine("M1");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "hello");
        var modified = new DateTime(2021, 3, 2, 10, 20, 30, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, modified);

        var entries = _lister.List(dir).OrderBy(e => e.Name).ToList();

        Assert.Equal(["a.txt", "sub"], entries.Select(e => e.Name));
        var a = entries[0];
        Assert.False(a.IsDirectory);
        Assert.Equal(5, a.Size);
        Assert.Equal(new DateTimeOffset(modified), a.ModifiedUtc);
        Assert.Equal(TimeSpan.Zero, a.ModifiedUtc.Offset);
        Assert.Equal(new FileInfo(file).LastAccessTimeUtc, a.AccessedUtc.UtcDateTime);
        Assert.True(entries[1].IsDirectory);
        Assert.Equal(0, entries[1].Size);
    }

    [Fact]
    public void Empty_folder_lists_nothing()
    {
        var dir = _temp.Combine("empty");
        Directory.CreateDirectory(dir);

        Assert.Empty(_lister.List(dir));
    }

    [Fact]
    public void Hidden_files_are_included()
    {
        var dir = _temp.Combine("M1");
        Directory.CreateDirectory(dir);
        var hidden = Path.Combine(dir, ".hidden");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Contains(_lister.List(dir), e => e.Name == ".hidden");
    }

    [Fact]
    public void Symbolic_link_is_reported_as_reparse_point()
    {
        var dir = _temp.Combine("M1");
        var target = _temp.Combine("target");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "outside.txt"), "x");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(dir, "link"), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
        }

        var link = Assert.Single(_lister.List(dir));
        Assert.True(link.IsReparsePoint);
        Assert.True(link.IsDirectory);
    }

    [Fact]
    public void Paths_longer_than_260_characters_are_listed()
    {
        var dir = _temp.Path;
        while (dir.Length < 300)
        {
            dir = Path.Combine(dir, new string('d', 40));
        }

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "deep.txt"), "x");

        Assert.True(Path.Combine(dir, "deep.txt").Length > 260);
        Assert.Equal("deep.txt", Assert.Single(_lister.List(dir)).Name);
    }

    [Fact]
    public void Missing_folder_throws_directory_not_found()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _lister.List(_temp.Combine("nope")));
        Assert.Throws<DirectoryNotFoundException>(() => _lister.GetDirectory(_temp.Combine("nope")));
    }

    [Fact]
    public void GetDirectory_returns_folder_metadata()
    {
        var dir = _temp.Combine("M1");
        Directory.CreateDirectory(dir);

        var entry = _lister.GetDirectory(dir);

        Assert.Equal("M1", entry.Name);
        Assert.True(entry.IsDirectory);
        Assert.Equal(Directory.GetLastWriteTimeUtc(dir), entry.ModifiedUtc.UtcDateTime);
    }
}

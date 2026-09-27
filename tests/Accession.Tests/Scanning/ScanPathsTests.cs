using Accession.Core.Scanning;

namespace Accession.Tests.Scanning;

public class ScanPathsTests
{
    [Theory]
    [InlineData("report.PDF", "pdf")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("README", "")]
    [InlineData("file.", "")]
    [InlineData(".gitignore", "gitignore")]
    [InlineData("box_03.E01", "e01")]
    public void Extension_rule(string name, string expected)
    {
        Assert.Equal(expected, ScanPaths.Extension(name));
    }

    [Fact]
    public void Relative_paths_start_with_media_and_use_backslashes()
    {
        var media = ScanPaths.MediaFolder("123-123_001");
        var files = ScanPaths.ChildFolder(media, "files");

        Assert.Equal(@"\123-123_001\", media);
        Assert.Equal(@"\123-123_001\files\", files);
        Assert.Equal(@"\123-123_001\files\a.msg", ScanPaths.File(files, "a.msg"));
    }

    [Fact]
    public void Full_path_uses_the_os_separator()
    {
        var root = Path.Combine(Path.GetTempPath(), "Media");

        Assert.Equal(Path.Combine(root, "M1", "files", "a.msg"), ScanPaths.ToFullPath(root, @"\M1\files\a.msg"));
        Assert.Equal(Path.Combine(root, "M1"), ScanPaths.ToFullPath(root, @"\M1\"));
    }
}

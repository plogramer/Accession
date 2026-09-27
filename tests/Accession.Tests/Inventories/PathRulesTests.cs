using Accession.Core.Inventories;

namespace Accession.Tests.Inventories;

public class PathRulesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "cases", "ACME", "Media");

    [Fact]
    public void File_in_root_is_allowed()
    {
        Assert.False(PathRules.IsInsideMediaFolder(Path.Combine(Root, "inv.sqlite"), Root));
    }

    [Fact]
    public void File_in_a_media_folder_or_deeper_is_rejected()
    {
        Assert.True(PathRules.IsInsideMediaFolder(Path.Combine(Root, "123-123_001", "inv.sqlite"), Root));
        Assert.True(PathRules.IsInsideMediaFolder(Path.Combine(Root, "123-123_001", "sub", "inv.sqlite"), Root));
    }

    [Fact]
    public void File_outside_root_is_allowed()
    {
        Assert.False(PathRules.IsInsideMediaFolder(Path.Combine(Path.GetTempPath(), "cases", "inv.sqlite"), Root));
    }

    [Fact]
    public void Sibling_folder_with_common_prefix_is_not_inside()
    {
        Assert.False(PathRules.IsUnder(Root + "Backup", Root));
        Assert.False(PathRules.IsInsideMediaFolder(Path.Combine(Root + "Backup", "inv.sqlite"), Root));
    }

    [Fact]
    public void Trailing_separator_on_root_is_ignored()
    {
        var rootWithSlash = Root + Path.DirectorySeparatorChar;

        Assert.True(PathRules.IsUnder(Path.Combine(Root, "M1"), rootWithSlash));
        Assert.True(PathRules.AreSameDirectory(Root, rootWithSlash));
    }
}

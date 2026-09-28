using System.Reflection;
using System.Text.RegularExpressions;
using Accession.UI.App;

namespace Accession.Tests.WebUi;

/// <summary>The bundled help (src/Accession.App/wwwroot/help): every topic the app opens exists, every picture is there.</summary>
public sealed partial class HelpContentTests
{
    private static readonly string HelpFolder = Path.Combine(RepoRoot(), "src", "Accession.App", "wwwroot", "help");
    private static readonly string Page = File.ReadAllText(Path.Combine(HelpFolder, "index.html"));

    [Fact]
    public void Every_help_topic_has_a_section()
    {
        var topics = typeof(HelpTopics).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.NotEmpty(topics);
        Assert.All(topics, topic => Assert.Contains($"<section id=\"{topic}\">", Page, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_screen_opens_its_own_topic()
    {
        foreach (var icon in new[] { "dashboard", "media", "files", "categories", "queue", "errors", "audit" })
        {
            Assert.Equal(icon, HelpTopics.ForScreen(icon));
        }

        Assert.Equal(HelpTopics.Contents, HelpTopics.ForScreen("unknown"));
    }

    [Fact]
    public void Every_picture_and_menu_link_resolves()
    {
        var images = ImageSource().Matches(Page).Select(m => m.Groups[1].Value).ToList();
        Assert.True(images.Count >= 20, $"Only {images.Count} pictures.");
        Assert.All(images, image => Assert.True(File.Exists(Path.Combine(HelpFolder, image)), $"Missing {image}"));

        var ids = SectionId().Matches(Page).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.All(Anchor().Matches(Page).Select(m => m.Groups[1].Value), anchor => Assert.Contains(anchor, ids));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Accession.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Accession.sln not found above the test folder.");
    }

    [GeneratedRegex("<img src=\"([^\"]+)\"")]
    private static partial Regex ImageSource();

    [GeneratedRegex("<section id=\"([^\"]+)\"")]
    private static partial Regex SectionId();

    [GeneratedRegex("href=\"#([^\"]+)\"")]
    private static partial Regex Anchor();
}

using Accession.Core.Copying;

namespace Accession.Tests.Copying;

/// <summary>Naming, destination guard and copy command rules (CPY-02, CPY-04, CPY-05).</summary>
public sealed class CopyRulesTests
{
    [Theory]
    [InlineData("abc.txt", 2, "abc_2_.txt")]
    [InlineData("abc.txt", 13, "abc_13_.txt")]
    [InlineData("report.final.PDF", 3, "report.final_3_.PDF")]
    [InlineData("README", 2, "README_2_")]
    [InlineData(".profile", 2, ".profile_2_")]
    public void Quick_copy_numbers_a_taken_name_before_its_extension(string name, int number, string expected) =>
        Assert.Equal(expected, CopyNaming.NumberedName(name, number));

    [Fact]
    public void Quick_copy_takes_the_first_free_name_ignoring_case()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "abc.txt", "ABC_2_.TXT", "abc_4_.txt" };
        Assert.Equal("abc_3_.txt", CopyNaming.FreeName("abc.txt", taken.Contains));
        Assert.Equal("new.txt", CopyNaming.FreeName("new.txt", taken.Contains));
    }

    [Fact]
    public void Sequential_names_are_padded_and_keep_the_extension()
    {
        var naming = new CopyNaming { Mode = CopyNamingMode.Sequential, Prefix = "ABC_", Digits = 8, StartNumber = 1 };

        Assert.Equal("ABC_00000001.pdf", naming.SequentialName(1, "pdf"));
        Assert.Equal("ABC_00012345.MSG", naming.SequentialName(12_345, "MSG"));
        Assert.Equal("ABC_00000007", naming.SequentialName(7, string.Empty)); // no dot without an extension
        Assert.Null(naming.Validate(99_999_999));
        Assert.Contains("does not fit in 8 digits", naming.Validate(100_000_000), StringComparison.Ordinal);
        Assert.Contains("not allowed", (naming with { Prefix = "a:b" }).Validate(1), StringComparison.Ordinal);
        Assert.NotNull((naming with { Digits = 0 }).Validate(1));
        Assert.Null(CopyNaming.Preserve.Validate(long.MaxValue));
    }

    [Fact]
    public void The_destination_can_never_be_the_root_or_inside_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "Evidence");

        Assert.NotNull(CopyPaths.ValidateDestination(root, root));
        Assert.NotNull(CopyPaths.ValidateDestination(Path.Combine(root, "MED001", "out"), root));
        Assert.Null(CopyPaths.ValidateDestination(Path.Combine(Path.GetTempPath(), "Evidence2"), root)); // shares the prefix only
        Assert.Null(CopyPaths.ValidateDestination(Path.Combine(Path.GetTempPath(), "Out"), root));
        Assert.NotNull(CopyPaths.ValidateDestination("relative\\folder", root));
        Assert.NotNull(CopyPaths.ValidateDestination(" ", root));
    }

    [Fact]
    public void Commands_quote_paths_and_double_percent_signs()
    {
        var source = Path.Combine("C:", "Ev", "100% & done.msg");
        var destination = Path.Combine("D:", "Out", "DOC1.msg");

        Assert.Equal($"copy /Y \"{source.Replace("%", "%%", StringComparison.Ordinal)}\" \"{destination}\" >nul", CopyCommandTemplate.Copy.Render(source, destination));

        // Already quoted placeholders are not quoted twice; the user's own % is escaped too.
        var custom = new CopyCommandTemplate("custom", "xcopy \"{source}\" {destdir} /K %x");
        Assert.Equal($"xcopy \"{source.Replace("%", "%%", StringComparison.Ordinal)}\" \"{Path.GetDirectoryName(destination)}\" /K %%x", custom.Render(source, destination));

        var robocopy = CopyCommandTemplate.Robocopy.Render(source, destination);
        Assert.StartsWith($"robocopy \"{Path.GetDirectoryName(source)}\" \"{Path.GetDirectoryName(destination)}\" \"100%% & done.msg\" ", robocopy, StringComparison.Ordinal);
    }

    [Fact]
    public void Robocopy_cannot_rename_and_commands_need_their_placeholders()
    {
        Assert.True(CopyCommandTemplate.Robocopy.IsRobocopy);
        Assert.Equal(8, CopyCommandTemplate.Robocopy.FailureExitCode);
        Assert.Equal(1, CopyCommandTemplate.Copy.FailureExitCode);
        Assert.Null(CopyCommandTemplate.Robocopy.Validate(CopyNamingMode.PreserveStructure));
        Assert.Contains("cannot rename", CopyCommandTemplate.Robocopy.Validate(CopyNamingMode.Sequential), StringComparison.Ordinal);
        Assert.Null(CopyCommandTemplate.Copy.Validate(CopyNamingMode.Sequential));

        Assert.NotNull(new CopyCommandTemplate("c", "copy {destination}").Validate(CopyNamingMode.PreserveStructure));
        Assert.NotNull(new CopyCommandTemplate("c", "copy {source}").Validate(CopyNamingMode.PreserveStructure));
        Assert.NotNull(new CopyCommandTemplate("c", "xcopy {source} {destdir}").Validate(CopyNamingMode.Sequential)); // would keep the old name
        Assert.Null(new CopyCommandTemplate("c", "copy -d -g -f {source} {destination}").Validate(CopyNamingMode.Sequential));
        Assert.NotNull(new CopyCommandTemplate("c", "copy {source}\r\n{destination}").Validate(CopyNamingMode.PreserveStructure));
    }
}

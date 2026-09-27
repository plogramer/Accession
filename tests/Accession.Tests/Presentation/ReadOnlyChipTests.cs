using Accession.Data.Locking;
using Accession.Presentation.ViewModels.Shell;

namespace Accession.Tests.Presentation;

/// <summary>The top bar's read-only chip: who has the inventory open for editing, and how to edit it.</summary>
public sealed class ReadOnlyChipTests
{
    private static LockHolder Holder(string user, string machine) =>
        new(user, machine, 42, Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Someone_else_is_named_with_their_computer()
    {
        var (text, tooltip) = WebShellViewModel.DescribeReadOnly(false, Holder(@"LITSUPPORT\john.roe", "LIT-PC12"), @"LITSUPPORT\jane.doe", "LIT-PC07");

        Assert.Equal("Read-only · john.roe on LIT-PC12", text);
        Assert.StartsWith(@"LITSUPPORT\john.roe has this inventory open for editing on LIT-PC12.", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Own_other_window_on_this_computer()
    {
        var (text, _) = WebShellViewModel.DescribeReadOnly(false, Holder(@"LITSUPPORT\JANE.DOE", "lit-pc07"), @"LITSUPPORT\jane.doe", "LIT-PC07");

        Assert.Equal("Read-only · open in another window", text);
    }

    [Fact]
    public void Free_lock_and_read_only_file_explain_how_to_edit()
    {
        Assert.Contains("close and reopen it to edit", WebShellViewModel.DescribeReadOnly(false, null, "u", "m").Tooltip, StringComparison.Ordinal);
        Assert.Contains("can't be written", WebShellViewModel.DescribeReadOnly(true, Holder("x", "y"), "u", "m").Tooltip, StringComparison.Ordinal);
    }
}

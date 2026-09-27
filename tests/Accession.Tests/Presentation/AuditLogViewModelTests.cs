using Accession.Core.Model;
using Accession.Presentation.Services;
using Accession.Presentation.ViewModels.Browsing;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Accession.Tests.Presentation;

/// <summary>Numbered pages (newest first) and filters of the Audit Log screen.</summary>
public sealed class AuditLogViewModelTests : IDisposable
{
    private readonly TestSession _test = new();
    private readonly InventoryHost _host = new(new InlineUiDispatcher());
    private readonly long _existing;

    public AuditLogViewModelTests()
    {
        _existing = _test.Session.Audit.Count(new());
        using (var scope = _test.Session.Database.Open())
        {
            using var transaction = scope.BeginTransaction();
            for (var i = 0; i < 1_200; i++)
            {
                _test.Session.Audit.Write(scope, i % 4 == 0 ? AuditAction.MediaDeleted : AuditAction.MediaAdded, $"M{i:0000}", new { index = i });
            }

            transaction.Commit();
        }

        _host.Open(_test.Session);
    }

    public void Dispose()
    {
        _host.Close();
        _test.Dispose();
    }

    [Fact]
    public void First_page_is_the_newest_entries()
    {
        using var vm = Create();

        Assert.Equal(1_200 + _existing, vm.TotalCount);
        Assert.Equal(500, vm.PageRows.Count);
        Assert.Equal("M1199", vm.PageRows[0].MediaId);
    }

    [Fact]
    public async Task Jump_to_the_last_page_and_back()
    {
        using var vm = Create();

        await vm.GoToPageAsync(99);
        Assert.Equal(2, vm.PageIndex);
        Assert.Equal((int)(1_200 + _existing - 1_000), vm.PageRows.Count);

        await vm.GoToPageAsync(1);
        Assert.Equal("M0699", vm.PageRows[0].MediaId);
    }

    [Fact]
    public void Filters_by_action_and_media()
    {
        using var vm = Create();

        vm.ActionFilterValue = nameof(AuditAction.MediaDeleted);
        Assert.Equal(300, vm.TotalCount);
        Assert.Contains(vm.ActionFilterOptions, o => o.Label == "Media deleted");

        vm.MediaIdText = "M0004";
        vm.ApplyMediaFilterCommand.Execute(null);
        Assert.Equal(1, vm.TotalCount);

        vm.SelectedRow = vm.PageRows[0];
        Assert.Contains("\"index\": 4", vm.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Date_filter_uses_local_dates()
    {
        using var vm = Create();

        vm.FromDateText = "2030-01-01";

        Assert.Equal(0, vm.TotalCount);
        Assert.Equal("2030-01-01", vm.FromDateText);
    }

    private AuditLogViewModel Create() => new(_host, new TestSettings(), NullLogger<AuditLogViewModel>.Instance);
}

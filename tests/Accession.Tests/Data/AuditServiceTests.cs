using System.Text.Json;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Repositories;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class AuditServiceTests : IDisposable
{
    private readonly TestInventory _inventory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeUser _user = new();
    private readonly AuditService _audit;

    public AuditServiceTests()
    {
        _audit = new AuditService(_inventory.Database, _user, _time);
    }

    public void Dispose() => _inventory.Dispose();

    private sealed class FakeUser : IUserContext
    {
        public string UserName { get; set; } = @"CORP\jdoe";
        public string MachineName { get; set; } = "WS-114";
    }

    [Fact]
    public void Entry_is_stamped_with_time_user_and_machine()
    {
        _audit.Write(AuditAction.MediaAdded, "123-123_001");

        var entry = Assert.Single(_audit.Query(new AuditQuery()));
        Assert.Equal(AuditAction.MediaAdded, entry.Action);
        Assert.Equal("123-123_001", entry.MediaId);
        Assert.Equal(_time.GetUtcNow(), entry.OccurredAtUtc);
        Assert.Equal(@"CORP\jdoe", entry.UserName);
        Assert.Equal("WS-114", entry.MachineName);
        Assert.Null(entry.Details);
    }

    [Fact]
    public void Details_are_stored_as_json_and_round_trip()
    {
        _audit.Write(AuditAction.RootPathChanged, details: new { OldPath = @"\\nas01\a", NewPath = @"\\nas02\a", MediaNotFound = new[] { "M3" } });

        var entry = Assert.Single(_audit.Query(new AuditQuery()));
        using var json = JsonDocument.Parse(entry.Details!);
        Assert.Equal(@"\\nas01\a", json.RootElement.GetProperty("oldPath").GetString());
        Assert.Equal(@"\\nas02\a", json.RootElement.GetProperty("newPath").GetString());
        Assert.Equal("M3", json.RootElement.GetProperty("mediaNotFound")[0].GetString());
    }

    [Fact]
    public void Write_inside_rolled_back_transaction_is_not_persisted()
    {
        using (var scope = _inventory.Database.Open())
        using (scope.BeginTransaction())
        {
            new MediaRepository(scope).Insert("M1", @"\M1\", _time.GetUtcNow(), "u");
            _audit.Write(scope, AuditAction.MediaAdded, "M1");
        }

        Assert.Empty(_audit.Query(new AuditQuery()));
    }

    [Fact]
    public void Write_inside_committed_transaction_is_persisted_with_the_change()
    {
        using (var scope = _inventory.Database.Open())
        using (var tx = scope.BeginTransaction())
        {
            new MediaRepository(scope).Insert("M1", @"\M1\", _time.GetUtcNow(), "u");
            _audit.Write(scope, AuditAction.MediaAdded, "M1");
            tx.Commit();
        }

        Assert.Single(_audit.Query(new AuditQuery()));
    }

    [Fact]
    public void Query_filters_by_date_action_user_and_media()
    {
        _audit.Write(AuditAction.InventoryCreated);
        _time.Advance(TimeSpan.FromHours(1));
        _audit.Write(AuditAction.MediaAdded, "M1");
        _user.UserName = @"CORP\asmith";
        _time.Advance(TimeSpan.FromHours(1));
        _audit.Write(AuditAction.MediaAdded, "M2");
        _audit.Write(AuditAction.ScanStarted, "m1");

        var start = new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(4, _audit.Query(new AuditQuery()).Count);
        Assert.Equal(2, _audit.Query(new AuditQuery { Actions = [AuditAction.MediaAdded] }).Count);
        Assert.Equal(2, _audit.Query(new AuditQuery { UserName = @"corp\ASMITH" }).Count);
        Assert.Equal(2, _audit.Query(new AuditQuery { MediaId = "M1" }).Count);
        Assert.Equal(AuditAction.MediaAdded,
            Assert.Single(_audit.Query(new AuditQuery { From = start.AddMinutes(30), To = start.AddMinutes(90) })).Action);
        Assert.Equal([@"CORP\asmith", @"CORP\jdoe"], _audit.ListUserNames());
    }

    [Fact]
    public void Query_returns_newest_first_with_keyset_paging()
    {
        for (var i = 0; i < 7; i++)
        {
            _audit.Write(AuditAction.ScanQueued, $"M{i}");
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var page1 = _audit.Query(new AuditQuery { PageSize = 4 });
        var page2 = _audit.Query(new AuditQuery { PageSize = 4, BeforeAuditId = page1[^1].AuditId });

        Assert.Equal(["M6", "M5", "M4", "M3"], page1.Select(e => e.MediaId));
        Assert.Equal(["M2", "M1", "M0"], page2.Select(e => e.MediaId));
    }

    [Fact]
    public void Audit_service_exposes_no_update_or_delete()
    {
        var methods = typeof(IAuditService).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methods, n => n.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}

using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Sessions;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class InventoryCreationServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly string _root;
    private readonly InventoryCreationService _service;

    public InventoryCreationServiceTests()
    {
        _root = _temp.Combine("Media");
        Directory.CreateDirectory(_root);
        var factory = new InventorySessionFactory(new User(), _time, NullLoggerFactory.Instance);
        _service = new InventoryCreationService(factory, new App());
    }

    public void Dispose() => _temp.Dispose();

    private sealed class User : IUserContext
    {
        public string UserName => @"CORP\jdoe";
        public string MachineName => "WS-114";
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    private CreateInventoryRequest Request(string? savePath = null) => new(
        " ACME Corporation ", "ACME", "Smith v. ACME", "2026-001", "  ", "https://pm.example.com/m/1",
        _root + Path.DirectorySeparatorChar, savePath ?? Path.Combine(_root, "ACME_2026-001_Inventory.sqlite"));

    [Fact]
    public void Creates_a_locked_writable_session()
    {
        using var session = _service.Create(Request());

        Assert.True(File.Exists(session.DbPath));
        Assert.False(session.IsReadOnly);
        Assert.True(session.LockService!.IsHeld);
        Assert.Equal(_root, session.Config.RootPath); // trailing separator removed
        Assert.Equal("ACME Corporation", session.Config.ClientName); // trimmed
        Assert.Null(session.Config.Description); // blank -> null
        Assert.Equal(@"CORP\jdoe", session.Config.CreatedBy);
        Assert.Equal("WS-114", session.Config.CreatedOnMachine);
        Assert.Equal("0.1.0", session.Config.CreatedAppVersion);
        Assert.Equal(_time.GetUtcNow(), session.Config.CreatedAtUtc);
        Assert.Equal(_time.GetUtcNow(), session.Config.LastOpenedAtUtc);
        Assert.Equal(session.DbPath, session.Config.LastDbPath);
    }

    [Fact]
    public void Creation_is_audited()
    {
        using var session = _service.Create(Request());

        var actions = session.Audit.Query(new AuditQuery()).Select(a => a.Action).Reverse().ToList();
        Assert.Equal([AuditAction.InventoryCreated, AuditAction.LockAcquired], actions);
        var created = session.Audit.Query(new AuditQuery { Actions = [AuditAction.InventoryCreated] }).Single();
        Assert.Contains("\"clientCode\":\"ACME\"", created.Details);
    }

    [Fact]
    public void Close_audits_and_releases_the_lock()
    {
        var session = _service.Create(Request());

        session.Close();
        session.Close(); // idempotent

        Assert.False(session.LockService!.IsHeld);
        Assert.Null(session.LockService.ReadHolder());
        var actions = session.Audit.Query(new AuditQuery()).Select(a => a.Action).ToList();
        Assert.Equal([AuditAction.LockReleased, AuditAction.InventoryClosed, AuditAction.LockAcquired, AuditAction.InventoryCreated], actions);
    }

    [Fact]
    public void Invalid_request_throws_with_field_errors_and_creates_nothing()
    {
        var bad = Request(Path.Combine(_root, "M1", "inv.sqlite")) with { ClientCode = "" };
        Directory.CreateDirectory(Path.Combine(_root, "M1"));

        var ex = Assert.Throws<InventoryValidationException>(() => _service.Create(bad));

        Assert.Contains(InventoryFields.ClientCode, ex.Errors.Keys);
        Assert.Contains(InventoryFields.SavePath, ex.Errors.Keys);
        Assert.Empty(Directory.GetFiles(_root, "*.sqlite", SearchOption.AllDirectories));
    }
}

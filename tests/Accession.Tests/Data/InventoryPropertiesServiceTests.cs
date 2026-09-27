using Accession.Core.Inventories;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Sessions;
using Accession.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class InventoryPropertiesServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly InventorySession _session;
    private readonly InventoryPropertiesService _service = new();

    public InventoryPropertiesServiceTests()
    {
        var root = _temp.Combine("Media");
        Directory.CreateDirectory(root);
        var factory = new InventorySessionFactory(new User(), new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(56)), NullLoggerFactory.Instance);
        _session = new InventoryCreationService(factory, new App()).Create(new CreateInventoryRequest(
            "ACME Corporation", "ACME", "Smith v. ACME", "2026-001", "Original", null, root, Path.Combine(root, "inv.sqlite")));
    }

    public void Dispose()
    {
        _session.Close();
        _temp.Dispose();
    }

    private sealed class User : IUserContext
    {
        public string UserName => @"CORP\jdoe";
        public string MachineName => "WS-114";
    }

    private sealed class App : IAppInfo
    {
        public string Version => "0.1.0";
    }

    private MatterInfo Current() => new(
        _session.Config.ClientName, _session.Config.ClientCode, _session.Config.MatterName,
        _session.Config.MatterCode, _session.Config.Description, _session.Config.MatterUrl);

    [Fact]
    public void Changed_fields_are_saved_and_audited_with_old_and_new_values()
    {
        var changed = _service.UpdateMatter(_session, Current() with { MatterName = " Smith v. ACME Corp ", MatterUrl = "https://pm/1" });

        Assert.True(changed);
        Assert.Equal("Smith v. ACME Corp", _session.Config.MatterName);
        Assert.Equal("https://pm/1", _session.Config.MatterUrl);
        var entry = _session.Audit.Query(new AuditQuery { Actions = [AuditAction.ConfigUpdated] }).Single();
        Assert.Contains("\"MatterName\":{\"old\":\"Smith v. ACME\",\"new\":\"Smith v. ACME Corp\"}", entry.Details);
        Assert.Contains("\"MatterUrl\":{\"old\":null,\"new\":\"https://pm/1\"}", entry.Details);
        Assert.DoesNotContain("ClientName", entry.Details);
    }

    [Fact]
    public void No_changes_writes_nothing()
    {
        Assert.False(_service.UpdateMatter(_session, Current() with { ClientName = " ACME Corporation " }));
        Assert.Empty(_session.Audit.Query(new AuditQuery { Actions = [AuditAction.ConfigUpdated] }));
    }

    [Fact]
    public void Clearing_description_stores_null()
    {
        _service.UpdateMatter(_session, Current() with { Description = "   " });

        Assert.Null(_session.Config.Description);
    }

    [Fact]
    public void Invalid_values_are_rejected()
    {
        var ex = Assert.Throws<InventoryValidationException>(() =>
            _service.UpdateMatter(_session, Current() with { ClientCode = "", MatterUrl = "not a url" }));

        Assert.Contains(InventoryFields.ClientCode, ex.Errors.Keys);
        Assert.Contains(InventoryFields.MatterUrl, ex.Errors.Keys);
        Assert.Equal("ACME", _session.Config.ClientCode);
    }
}

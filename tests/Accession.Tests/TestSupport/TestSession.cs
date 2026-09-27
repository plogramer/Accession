using Accession.Core.Inventories;
using Accession.Core.Runtime;
using Accession.Data.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.TestSupport;

/// <summary>A newly created, locked inventory session over a temp root folder.</summary>
public sealed class TestSession : IDisposable
{
    private readonly TempDirectory _temp = new();

    public TestSession()
    {
        Root = _temp.Combine("Media");
        Directory.CreateDirectory(Root);
        Factory = new InventorySessionFactory(new User(), Time, NullLoggerFactory.Instance);
        Session = new InventoryCreationService(Factory, new App()).Create(new CreateInventoryRequest(
            "ACME Corporation", "ACME", "Smith v. ACME", "2026-001", null, null, Root, _temp.Combine("inv.sqlite")));
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    public string Root { get; }
    public InventorySessionFactory Factory { get; }
    public InventorySession Session { get; }
    public TempDirectory Temp => _temp;

    /// <summary>Creates media folders under the root.</summary>
    public void CreateFolders(params string[] names)
    {
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(Root, name));
        }
    }

    public void Dispose()
    {
        Session.Close();
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
}

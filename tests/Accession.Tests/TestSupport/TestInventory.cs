using Accession.Core.Model;
using Accession.Data;
using Accession.Data.Schema;

namespace Accession.Tests.TestSupport;

/// <summary>Creates a fresh inventory file in a temp folder for data tests.</summary>
public sealed class TestInventory : IDisposable
{
    public static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 9, 27, 14, 3, 12, TimeSpan.Zero).AddTicks(1234567);

    private readonly TempDirectory _temp = new();

    public TestInventory()
    {
        RootPath = _temp.Combine("Media");
        Directory.CreateDirectory(RootPath);
        DbPath = _temp.Combine("Inventory.sqlite");
        Config = NewConfig(RootPath);
        DatabaseCreator.Create(DbPath, Config);
        Database = new InventoryDatabase(DbPath);
    }

    public string RootPath { get; }
    public string DbPath { get; }
    public InventoryConfig Config { get; }
    public InventoryDatabase Database { get; }
    public TempDirectory Temp => _temp;

    public static InventoryConfig NewConfig(string rootPath) => new()
    {
        InventoryGuid = Guid.Parse("7d3f2a8e-1b4c-4f5d-9e6a-0c1b2d3e4f5a"),
        RootPath = rootPath,
        ClientName = "ACME Corporation",
        ClientCode = "ACME",
        MatterName = "Smith v. ACME",
        MatterCode = "2026-001",
        Description = "Test inventory",
        MatterUrl = "https://pm.example.com/matters/2026-001",
        CreatedAtUtc = CreatedAt,
        CreatedBy = @"CORP\jdoe",
        CreatedOnMachine = "WS-114",
        CreatedAppVersion = "0.1.0",
    };

    public void Dispose() => _temp.Dispose();
}

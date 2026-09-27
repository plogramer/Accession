using Accession.Core.Inventories;

namespace Accession.Tests.Inventories;

public class InventoryFileNameTests
{
    [Theory]
    [InlineData("ACME", "2026-001", "ACME_2026-001_Inventory.sqlite")]
    [InlineData(" ACME ", " 2026/001 ", "ACME_2026_001_Inventory.sqlite")]
    [InlineData("A:B*C?", "M<1>|\"x\"", "A_B_C__M_1___x__Inventory.sqlite")]
    [InlineData("ACME.", "", "ACME_Inventory.sqlite")]
    [InlineData("", "", "Inventory.sqlite")]
    [InlineData(null, "M1", "M1_Inventory.sqlite")]
    public void Default_name_follows_INV02(string? client, string? matter, string expected)
    {
        Assert.Equal(expected, InventoryFileName.Default(client, matter));
    }
}

using Accession.Core.Inventories;

namespace Accession.Tests.Inventories;

public class InventoryFileNameTests
{
    [Theory]
    [InlineData("ACME", "2026-001", "ACME_2026-001_Inventory.accession")]
    [InlineData(" ACME ", " 2026/001 ", "ACME_2026_001_Inventory.accession")]
    [InlineData("A:B*C?", "M<1>|\"x\"", "A_B_C__M_1___x__Inventory.accession")]
    [InlineData("ACME.", "", "ACME_Inventory.accession")]
    [InlineData("", "", "Inventory.accession")]
    [InlineData(null, "M1", "M1_Inventory.accession")]
    public void Default_name_follows_INV02(string? client, string? matter, string expected)
    {
        Assert.Equal(expected, InventoryFileName.Default(client, matter));
    }

    [Theory]
    [InlineData(@"D:\Cases\ACME", @"D:\Cases\ACME.accession")] // typed without an extension
    [InlineData(@"D:\Cases\ACME.accession", @"D:\Cases\ACME.accession")]
    [InlineData(@"D:\Cases\old.sqlite", @"D:\Cases\old.sqlite")] // an extension the user chose stays
    [InlineData("", "")]
    public void A_path_without_extension_gets_accession(string typed, string expected)
    {
        Assert.Equal(expected, InventoryFileName.WithExtension(typed));
    }

    [Fact]
    public void The_open_dialog_lists_new_and_older_inventories()
    {
        Assert.Contains("*.accession;*.sqlite", InventoryFileName.OpenFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("sqlite", InventoryFileName.SaveFilter, StringComparison.Ordinal);
    }
}

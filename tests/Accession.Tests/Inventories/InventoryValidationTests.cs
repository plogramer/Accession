using Accession.Core.Inventories;
using Accession.Tests.TestSupport;

namespace Accession.Tests.Inventories;

public sealed class InventoryValidationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _root;

    public InventoryValidationTests()
    {
        _root = _temp.Combine("Media");
        Directory.CreateDirectory(Path.Combine(_root, "123-123_001"));
    }

    public void Dispose() => _temp.Dispose();

    private CreateInventoryRequest Valid() => new(
        "ACME Corporation", "ACME", "Smith v. ACME", "2026-001", null, "https://pm.example.com/m/1",
        _root, Path.Combine(_root, "ACME_2026-001_Inventory.sqlite"));

    [Fact]
    public void Valid_request_has_no_errors()
    {
        Assert.Empty(InventoryValidation.ValidateCreate(Valid()));
    }

    [Fact]
    public void Required_fields_are_reported()
    {
        var errors = InventoryValidation.ValidateCreate(Valid() with { ClientName = " ", ClientCode = "", MatterName = "", MatterCode = "" });

        Assert.Equal("Client Name is required.", errors[InventoryFields.ClientName]);
        Assert.Equal("Client ID is required.", errors[InventoryFields.ClientCode]);
        Assert.Equal("Matter Name is required.", errors[InventoryFields.MatterName]);
        Assert.Equal("Matter ID is required.", errors[InventoryFields.MatterCode]);
    }

    [Fact]
    public void Overlong_field_is_reported()
    {
        var errors = InventoryValidation.ValidateCreate(Valid() with { MatterName = new string('x', 201) });

        Assert.Contains("200 characters", errors[InventoryFields.MatterName]);
    }

    [Theory]
    [InlineData("pm.example.com/matters/1")]
    [InlineData("ftp://pm.example.com/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    public void Matter_url_must_be_http_or_https(string url)
    {
        var errors = InventoryValidation.ValidateCreate(Valid() with { MatterUrl = url });

        Assert.Contains(InventoryFields.MatterUrl, errors.Keys);
    }

    [Fact]
    public void Empty_matter_url_is_allowed()
    {
        Assert.Empty(InventoryValidation.ValidateCreate(Valid() with { MatterUrl = "  " }));
    }

    [Fact]
    public void Root_must_exist()
    {
        var errors = InventoryValidation.ValidateCreate(Valid() with { RootFolder = _temp.Combine("nope") });

        Assert.Contains("does not exist", errors[InventoryFields.RootFolder]);
    }

    [Fact]
    public void Save_path_inside_media_folder_is_rejected()
    {
        var errors = InventoryValidation.ValidateCreate(Valid() with { SavePath = Path.Combine(_root, "123-123_001", "inv.sqlite") });

        Assert.Contains("inside a media folder", errors[InventoryFields.SavePath]);
    }

    [Fact]
    public void Save_path_outside_root_is_allowed()
    {
        Assert.Empty(InventoryValidation.ValidateCreate(Valid() with { SavePath = _temp.Combine("inv.sqlite") }));
    }

    [Fact]
    public void Existing_file_is_rejected()
    {
        var path = Path.Combine(_root, "existing.sqlite");
        File.WriteAllText(path, "x");

        var errors = InventoryValidation.ValidateCreate(Valid() with { SavePath = path });

        Assert.Contains("already exists", errors[InventoryFields.SavePath]);
    }

    [Fact]
    public void Missing_folder_or_folder_path_is_rejected()
    {
        Assert.Contains("folder does not exist",
            InventoryValidation.ValidateCreate(Valid() with { SavePath = _temp.Combine("no", "inv.sqlite") })[InventoryFields.SavePath]);
        Assert.Contains("is a folder",
            InventoryValidation.ValidateCreate(Valid() with { SavePath = _root })[InventoryFields.SavePath]);
        Assert.Contains("Choose where",
            InventoryValidation.ValidateCreate(Valid() with { SavePath = "" })[InventoryFields.SavePath]);
    }
}

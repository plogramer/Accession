using Accession.Data.Schema;
using Accession.Tests.TestSupport;
using Dapper;

namespace Accession.Tests.Data;

public sealed class CategoryTests : IDisposable
{
    private readonly TestInventory _inventory = new();

    public void Dispose() => _inventory.Dispose();

    [Fact]
    public void Seeds_twenty_categories_in_appendix_order()
    {
        using var scope = _inventory.Database.Open();
        var names = scope.Connection.Query<string>("SELECT Name FROM FileCategory ORDER BY SortOrder").ToList();

        Assert.Equal(20, names.Count);
        Assert.Equal("Email", names[0]);
        Assert.Equal("Chat", names[1]);
        Assert.Equal("System Files", names[17]);
        Assert.Equal("No Extension", names[18]);
        Assert.Equal("Other / Unknown", names[19]);
    }

    [Fact]
    public void Every_extension_maps_to_exactly_one_category()
    {
        using var scope = _inventory.Database.Open();
        var rows = scope.Connection.Query<string>("SELECT Extension FROM ExtensionCategory").ToList();

        Assert.Equal(rows.Count, rows.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(CategoryCatalog.ExtensionMap.Count, rows.Count);
        Assert.All(rows, e => Assert.Equal(e.ToLowerInvariant(), e));
        Assert.All(rows, e => Assert.False(e.StartsWith('.')));
    }

    [Fact]
    public void Query_time_categories_have_no_mappings()
    {
        using var scope = _inventory.Database.Open();
        var count = scope.Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM ExtensionCategory WHERE CategoryId IN (@NoExt, @Other)",
            new { NoExt = CategoryCatalog.NoExtensionId, Other = CategoryCatalog.OtherUnknownId });

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("msg", "Email")]
    [InlineData("pst", "Email")]
    [InlineData("rsmf", "Chat")]
    [InlineData("xlsx", "Spreadsheets")]
    [InlineData("123", "Spreadsheets")]
    [InlineData("ts", "Video")]
    [InlineData("db", "Databases")]
    [InlineData("e07", "Forensic Images")]
    [InlineData("e99", "Forensic Images")]
    [InlineData("l01", "Forensic Images")]
    [InlineData("001", "Forensic Images")]
    [InlineData("999", "Forensic Images")]
    [InlineData("ex01", "Forensic Images")]
    [InlineData("ds_store", "System Files")]
    [InlineData("PDF", "PDF & Fixed Layout")]
    [InlineData("zzz", "Other / Unknown")]
    [InlineData("", "No Extension")]
    public void Category_rule_resolves_extensions(string extension, string expected)
    {
        using var scope = _inventory.Database.Open();
        var name = scope.Connection.QuerySingle<string>(
            $"SELECT c.Name FROM (SELECT @Extension AS Extension) x {CategorySql.JoinCategory("x.Extension", "c")}",
            new { Extension = extension });

        Assert.Equal(expected, name);
    }

    [Fact]
    public void Category_join_never_drops_or_duplicates_rows()
    {
        using var scope = _inventory.Database.Open();
        var count = scope.Connection.ExecuteScalar<long>(
            $"""
            WITH x(Extension) AS (VALUES ('msg'), (''), ('unknownext'), ('e01'), ('123'))
            SELECT COUNT(*) FROM x {CategorySql.JoinCategory("x.Extension", "c")}
            """);

        Assert.Equal(5, count);
    }
}

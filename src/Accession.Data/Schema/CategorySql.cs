namespace Accession.Data.Schema;

/// <summary>
/// The one rule for "category of an extension", shared by dashboard, file browser and export queries:
/// mapped extension → its category; empty extension → No Extension; anything else → Other / Unknown.
/// </summary>
public static class CategorySql
{
    /// <summary>
    /// SQL joins that bring in the category of <paramref name="extensionColumn"/> as table alias <paramref name="alias"/>
    /// (columns <c>CategoryId</c>, <c>Name</c>, <c>SortOrder</c>). Every row gets exactly one category.
    /// </summary>
    /// <example><c>SELECT f.Name, c.Name FROM File f {JoinCategory("f.Extension", "c")}</c></example>
    public static string JoinCategory(string extensionColumn, string alias = "fc")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        return $"""
            LEFT JOIN ExtensionCategory {alias}_map ON {alias}_map.Extension = {extensionColumn}
            JOIN FileCategory {alias} ON {alias}.CategoryId = COALESCE({alias}_map.CategoryId,
                CASE WHEN {extensionColumn} = '' THEN {CategoryCatalog.NoExtensionId} ELSE {CategoryCatalog.OtherUnknownId} END)
            """;
    }
}

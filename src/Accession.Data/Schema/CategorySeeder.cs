using Dapper;
using Microsoft.Data.Sqlite;

namespace Accession.Data.Schema;

/// <summary>Writes <see cref="CategoryCatalog"/> into <c>FileCategory</c> and <c>ExtensionCategory</c>.</summary>
public static class CategorySeeder
{
    /// <summary>Replaces all category rows with the catalog. Used at creation and by migrations that change categories.</summary>
    public static void Seed(SqliteConnection connection, SqliteTransaction transaction)
    {
        connection.Execute("DELETE FROM ExtensionCategory; DELETE FROM FileCategory;", transaction: transaction);

        connection.Execute(
            "INSERT INTO FileCategory (CategoryId, Name, Description, SortOrder) VALUES (@CategoryId, @Name, @Description, @CategoryId)",
            CategoryCatalog.Categories,
            transaction);

        connection.Execute(
            "INSERT INTO ExtensionCategory (Extension, CategoryId) VALUES (@Extension, @CategoryId)",
            CategoryCatalog.ExtensionMap.Select(e => new { Extension = e.Key, CategoryId = e.Value }),
            transaction);
    }
}

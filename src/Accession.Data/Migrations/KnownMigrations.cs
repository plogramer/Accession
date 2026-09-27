namespace Accession.Data.Migrations;

/// <summary>
/// Migrations shipped with this application, in order. Version 1 is the baseline created by
/// <see cref="Schema.DatabaseCreator"/>; add a class here (ToVersion = 2, 3, …) and bump
/// <see cref="Schema.InventorySchema.CurrentVersion"/> for every schema change.
/// </summary>
internal static class KnownMigrations
{
    public static IReadOnlyList<IMigration> All { get; } = [];
}

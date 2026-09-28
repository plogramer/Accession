namespace Accession.Data;

/// <summary>An inventory file opened by the application. Hands out <see cref="DbScope"/>s.</summary>
public sealed class InventoryDatabase
{
    public InventoryDatabase(string path, bool isReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        IsReadOnly = isReadOnly;
    }

    /// <summary>Full path of the inventory file (.accession, or .sqlite before version 0.2).</summary>
    public string Path { get; }

    public bool IsReadOnly { get; }

    public DbScope Open() => new(SqliteConnectionFactory.Open(Path, IsReadOnly));
}

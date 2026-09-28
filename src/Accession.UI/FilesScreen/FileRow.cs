namespace Accession.UI.FilesScreen;

/// <summary>A file in the Files table, with display-ready values.</summary>
public sealed record FileRow(
    long FileId,
    string MediaId,
    string Name,
    string Extension,
    string Category,
    string FolderPath,
    string RelativePath,
    string Size,
    string Created,
    string Modified,
    string Accessed,
    string HashStatus,
    string? Sha1,
    int Copies);

/// <summary>A choice in a select box. An empty <see cref="Value"/> means "any".</summary>
public sealed record SelectOption(string Value, string Label);

/// <summary>A saved search in the Files screen's list; <see cref="Created"/> reads "Created by … on …, date".</summary>
public sealed record SavedSearchRow(long Id, string Name, string Description, long FileCount, string Files, string Size, string Created = "")
{
    /// <summary>Tooltip: the description and who created it where.</summary>
    public string Tooltip => string.IsNullOrEmpty(Description) ? Created : $"{Description}\n{Created}";
}

/// <summary>An applied filter shown as a chip; <see cref="Key"/> is passed to the remove command (see <see cref="FilterKeys"/>).</summary>
public sealed record FilterChip(string Key, string Label);

/// <summary>Keys of the Files screen's filter chips.</summary>
public static class FilterKeys
{
    /// <summary>The media, folder, saved search or media set being shown.</summary>
    public const string Where = "where";
    public const string Category = "category";
    public const string Extension = "extension";
    public const string Name = "name";
    public const string MinSize = "minsize";
    public const string MaxSize = "maxsize";
    public const string ModifiedFrom = "from";
    public const string ModifiedTo = "to";
    public const string Hash = "hash";
    public const string Duplicates = "duplicates";
    public const string Errors = "errors";
    public const string Sha1 = "sha1";
}

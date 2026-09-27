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

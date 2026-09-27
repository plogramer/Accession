namespace Accession.Data.Migrations;

/// <summary>The inventory was created by a newer version of Accession.</summary>
public sealed class SchemaTooNewException(int fileVersion, int appVersion)
    : InvalidOperationException(
        $"This inventory uses schema version {fileVersion}, but this version of Accession supports up to version {appVersion}. " +
        "Update Accession to open it.")
{
    public int FileVersion { get; } = fileVersion;
    public int AppVersion { get; } = appVersion;
}

/// <summary>The file is not an Accession inventory.</summary>
public sealed class NotAnInventoryException(string path)
    : InvalidOperationException($"'{path}' is not an Accession inventory file.")
{
    public string Path { get; } = path;
}

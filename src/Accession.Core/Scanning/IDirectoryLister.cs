namespace Accession.Core.Scanning;

/// <summary>Lists one directory at a time. Never opens files, never follows reparse points.</summary>
public interface IDirectoryLister
{
    /// <summary>Entries of <paramref name="fullPath"/> (not recursive).</summary>
    /// <exception cref="UnauthorizedAccessException">Access denied.</exception>
    /// <exception cref="IOException">Not found, network or other I/O error (see <see cref="ScanErrorClassifier"/>).</exception>
    IReadOnlyList<DirectoryEntry> List(string fullPath);

    /// <summary>Metadata of a single directory (used for the media folder itself).</summary>
    DirectoryEntry GetDirectory(string fullPath);
}

namespace Accession.Core.Scanning;

/// <param name="Sha1">40 lowercase hex characters.</param>
/// <param name="SizeAfter">File length when hashing finished.</param>
/// <param name="ModifiedAfterUtc">Last write time when hashing finished.</param>
public sealed record FileHashResult(string Sha1, long BytesRead, long SizeAfter, DateTimeOffset ModifiedAfterUtc);

public interface IFileHasher
{
    /// <summary>Computes the SHA-1 of a file, opened read-only.</summary>
    FileHashResult Hash(string fullPath, CancellationToken cancellationToken);
}

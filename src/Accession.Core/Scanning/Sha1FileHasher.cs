using System.Buffers;
using System.Security.Cryptography;

namespace Accession.Core.Scanning;

/// <summary>
/// Streams a file through SHA-1 with a 1 MB buffer (SCN-21). Files are opened for reading only and shared for
/// read, write and delete so other programs are not blocked (SCN-40). On Windows the handle is first opened with
/// FILE_WRITE_ATTRIBUTES so the last-access time can be frozen (SetFileTime with 0xFFFFFFFF, SCN-42); if that is
/// not permitted the file is opened read-only and the access time may be updated by the file system.
/// </summary>
public sealed class Sha1FileHasher : IFileHasher
{
    public const int BufferSize = 1 << 20;

    public FileHashResult Hash(string fullPath, CancellationToken cancellationToken)
    {
        using var handle = EvidenceFile.OpenRead(fullPath);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            long offset = 0;
            int read;
            while ((read = RandomAccess.Read(handle, buffer.AsSpan(0, BufferSize), offset)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha1.AppendData(buffer, 0, read);
                offset += read;
            }

            return new FileHashResult(
                Convert.ToHexStringLower(sha1.GetHashAndReset()),
                offset,
                RandomAccess.GetLength(handle),
                new DateTimeOffset(File.GetLastWriteTimeUtc(handle), TimeSpan.Zero));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

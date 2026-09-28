using System.Buffers;
using System.Security.Cryptography;
using Accession.Core.Scanning;

namespace Accession.Core.Copying;

public enum CopyOutcome
{
    Copied,

    /// <summary>Copied, and the copy was read back and its SHA-1 matched the bytes read from the source.</summary>
    Verified,

    /// <summary>A file or folder already exists at the destination; it was left as it is.</summary>
    Skipped,

    Failed,
}

/// <param name="PreserveMetadata">Copy created, modified and accessed times and the attributes (read-only, hidden, system, archive).</param>
/// <param name="Verify">Read the copy back and compare its SHA-1 with the bytes read from the source (slower).</param>
public sealed record CopyFileOptions(bool PreserveMetadata = true, bool Verify = false);

/// <param name="Sha1">SHA-1 of the bytes read from the source; only computed when verifying.</param>
public sealed record CopyFileResult(CopyOutcome Outcome, long Bytes, string? Sha1 = null, string? Message = null);

/// <summary>
/// Copies one file out of the evidence (CPY-06). The source is only ever read, with the same sharing and access-time
/// rules as hashing (SCN-40, SCN-42). The destination is created new: an existing file is never overwritten. A partly
/// written copy (error or cancel) is deleted.
/// </summary>
public sealed class EvidenceFileCopier
{
    public const int BufferSize = 1 << 20;

    private const FileAttributes CopiedAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;

    /// <param name="bytesCopied">Called with the bytes written since the previous call.</param>
    public CopyFileResult Copy(string source, string destination, CopyFileOptions options, Action<long>? bytesCopied = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(options);

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            return new CopyFileResult(CopyOutcome.Skipped, 0, Message: "Already exists at the destination");
        }

        var created = false;
        try
        {
            using var input = EvidenceFile.OpenRead(source);

            // Read before copying: the frozen access time is the original one either way.
            var times = options.PreserveMetadata
                ? (Created: File.GetCreationTimeUtc(input), Modified: File.GetLastWriteTimeUtc(input), Accessed: File.GetLastAccessTimeUtc(input),
                    Attributes: File.GetAttributes(input))
                : default;

            if (Path.GetDirectoryName(destination) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            string? sha1;
            long length;
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
            {
                created = true;
                (length, sha1) = CopyBytes(input, output, options.Verify, bytesCopied, cancellationToken);
            }

            string? message = null;
            var outcome = CopyOutcome.Copied;
            if (options.Verify)
            {
                var copyHash = HashFile(destination, cancellationToken);
                if (!string.Equals(copyHash, sha1, StringComparison.Ordinal))
                {
                    TryDelete(destination);
                    return new CopyFileResult(CopyOutcome.Failed, length, sha1, $"Verification failed: the copy's SHA-1 {copyHash} differs from the source's {sha1}. The copy was deleted.");
                }

                outcome = CopyOutcome.Verified;
            }

            if (options.PreserveMetadata)
            {
                message = ApplyTimes(destination, times.Created, times.Modified, times.Accessed, times.Attributes);
            }

            return new CopyFileResult(outcome, length, sha1, message);
        }
        catch (OperationCanceledException)
        {
            if (created)
            {
                TryDelete(destination);
            }

            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            if (created)
            {
                TryDelete(destination);
            }

            return new CopyFileResult(CopyOutcome.Failed, 0, Message: ex.Message);
        }
    }

    /// <summary>Gives a destination folder the times of its source folder. Returns a warning, or null.</summary>
    public static string? CopyFolderTimes(string sourceFolder, string destinationFolder)
    {
        try
        {
            var created = Directory.GetCreationTimeUtc(sourceFolder);
            var modified = Directory.GetLastWriteTimeUtc(sourceFolder);
            var accessed = Directory.GetLastAccessTimeUtc(sourceFolder);
            Directory.SetCreationTimeUtc(destinationFolder, created);
            Directory.SetLastWriteTimeUtc(destinationFolder, modified);
            Directory.SetLastAccessTimeUtc(destinationFolder, accessed);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return ex.Message;
        }
    }

    private static (long Length, string? Sha1) CopyBytes(Microsoft.Win32.SafeHandles.SafeFileHandle input, FileStream output, bool hash,
        Action<long>? bytesCopied, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var sha1 = hash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
            long offset = 0;
            int read;
            while ((read = RandomAccess.Read(input, buffer.AsSpan(0, BufferSize), offset)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                sha1?.AppendData(buffer, 0, read);
                offset += read;
                bytesCopied?.Invoke(read);
            }

            output.Flush(flushToDisk: false);
            return (offset, sha1 is null ? null : Convert.ToHexStringLower(sha1.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
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

            return Convert.ToHexStringLower(sha1.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Times first (a read-only file's times cannot be changed), then attributes. Returns a warning, or null.</summary>
    private static string? ApplyTimes(string path, DateTime created, DateTime modified, DateTime accessed, FileAttributes attributes)
    {
        try
        {
            // Creation first: where the file system has no creation time, setting it may move the modified time.
            File.SetCreationTimeUtc(path, created);
            File.SetLastWriteTimeUtc(path, modified);
            File.SetLastAccessTimeUtc(path, accessed);
            var current = File.GetAttributes(path);
            var wanted = (current & ~CopiedAttributes) | (attributes & CopiedAttributes);
            if (wanted != current)
            {
                File.SetAttributes(path, wanted == 0 ? FileAttributes.Normal : wanted);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return "Copied, but the metadata could not be set: " + ex.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the user; the manifest reports the file as failed.
        }
    }
}

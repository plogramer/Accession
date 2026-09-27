using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

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
        using var handle = Open(fullPath);
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

    private static SafeFileHandle Open(string fullPath)
    {
        if (OperatingSystem.IsWindows() && TryOpenWithFrozenAccessTime(fullPath) is { } handle)
        {
            return handle;
        }

        return File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? TryOpenWithFrozenAccessTime(string fullPath)
    {
        const uint GenericRead = 0x80000000;
        const uint FileWriteAttributes = 0x00000100;
        const uint ShareAll = 0x00000007; // read | write | delete
        const uint OpenExisting = 3;
        const uint FlagSequentialScan = 0x08000000;

        var path = fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath
            : fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;

        var handle = CreateFileW(path, GenericRead | FileWriteAttributes, ShareAll, IntPtr.Zero, OpenExisting, FlagSequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null; // e.g. access denied for FILE_WRITE_ATTRIBUTES: fall back to plain read access
        }

        // 0xFFFFFFFF in both halves: do not update the last-access time for operations on this handle.
        var keep = new FileTime { Low = 0xFFFFFFFF, High = 0xFFFFFFFF };
        if (!SetFileTime(handle, IntPtr.Zero, ref keep, IntPtr.Zero))
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    [SupportedOSPlatform("windows")]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileTime(SafeFileHandle file, IntPtr creationTime, ref FileTime lastAccessTime, IntPtr lastWriteTime);
}

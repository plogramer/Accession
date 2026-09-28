using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Accession.Core.Scanning;

/// <summary>Read-only access to files under the root (SCN-40, SCN-42). Used by hashing and copying.</summary>
public static class EvidenceFile
{
    /// <summary>
    /// Opens an evidence file for reading: shared for read, write and delete (SCN-40), sequential, and on Windows with the
    /// last-access time frozen when permitted (SCN-42).
    /// </summary>
    public static SafeFileHandle OpenRead(string fullPath)
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

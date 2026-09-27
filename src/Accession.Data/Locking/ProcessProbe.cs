using System.Diagnostics;

namespace Accession.Data.Locking;

/// <summary>Tells whether a process recorded in the lock is still an Accession running on this computer.</summary>
public interface IProcessProbe
{
    bool IsAccessionRunning(int processId);
}

/// <summary>Checks the local process list: a process with that id running the same program (this process counts).</summary>
public sealed class SystemProcessProbe : IProcessProbe
{
    public static readonly SystemProcessProbe Instance = new();

    public bool IsAccessionRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        if (processId == Environment.ProcessId)
        {
            return true; // another session in this very process is alive
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            using var self = Process.GetCurrentProcess();
            return !process.HasExited && string.Equals(process.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false; // no such process (the id may also have been reused by another program)
        }
    }
}

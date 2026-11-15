using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OptiCore.Services;

public static class ProcessFreezer
{
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "smss", "csrss", "wininit", "services", "lsass",
        "svchost", "winlogon", "dwm", "explorer", "fontdrvhost", "audiodg",
        "conhost", "sihost", "taskhostw", "RuntimeBroker", "SearchHost",
        "StartMenuExperienceHost", "ShellExperienceHost", "ctfmon", "OptiCore"
    };

    private static readonly HashSet<int> FrozenPids = new();

    public static int FrozenCount => FrozenPids.Count;

    public static string? GetActiveGame(IEnumerable<string> games)
    {
        var gameSet = new HashSet<string>(games, StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (gameSet.Contains(proc.ProcessName))
                    return proc.ProcessName;
            }
            catch { }
            finally { proc.Dispose(); }
        }
        return null;
    }

    public static List<string> FreezeTargets(IEnumerable<string> targets, IEnumerable<string> whitelist, IEnumerable<string> games)
    {
        var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
        var whiteSet = new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase);
        var gameSet = new HashSet<string>(games, StringComparer.OrdinalIgnoreCase);
        var newlyFrozen = new List<string>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                string name = proc.ProcessName;
                if (proc.Id <= 4) continue;
                if (CriticalSystemProcesses.Contains(name)) continue;
                if (whiteSet.Contains(name)) continue;
                if (gameSet.Contains(name)) continue;
                if (!targetSet.Contains(name)) continue;
                if (FrozenPids.Contains(proc.Id)) continue;

                IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, proc.Id);
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        if (NtSuspendProcess(handle) == 0)
                        {
                            FrozenPids.Add(proc.Id);
                            if (!newlyFrozen.Contains(name))
                                newlyFrozen.Add(name);
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }

        return newlyFrozen;
    }

    public static int UnfreezeAll()
    {
        int resumed = 0;
        foreach (int pid in FrozenPids.ToList())
        {
            try
            {
                IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        if (NtResumeProcess(handle) == 0)
                            resumed++;
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
            catch { }
        }
        FrozenPids.Clear();
        return resumed;
    }
}

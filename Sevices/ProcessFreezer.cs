using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OptiCore.Services;

public class ProcessInfoItem
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public double RamMb { get; set; }
    public double CpuPercent { get; set; }
    public string Status { get; set; } = "Активен";
}

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
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "winlogon", "dwm", "explorer", "fontdrvhost",
        "audiodg", "conhost", "sihost", "taskhostw", "RuntimeBroker", "SearchHost",
        "StartMenuExperienceHost", "ShellExperienceHost", "ctfmon", "MsMpEng", "OptiCore"
    };

    private static readonly HashSet<int> FrozenPids = new();
    private static readonly Dictionary<int, (TimeSpan Cpu, DateTime Time)> CpuSnapshots = new();

    public static int FrozenCount => FrozenPids.Count;

    public static bool IsProtected(string processName) => CriticalSystemProcesses.Contains(processName);

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

    public static void SetGameHighPriority(string gameName, bool high)
    {
        foreach (var proc in Process.GetProcessesByName(gameName))
        {
            try
            {
                proc.PriorityClass = high ? ProcessPriorityClass.High : ProcessPriorityClass.Normal;
            }
            catch { }
            finally { proc.Dispose(); }
        }
    }

    public static List<ProcessInfoItem> GetRunningProcesses()
    {
        var result = new List<ProcessInfoItem>();
        var now = DateTime.UtcNow;
        int cores = Math.Max(1, Environment.ProcessorCount);

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4) continue;
                double ramMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
                double cpuPct = 0;

                try
                {
                    var totalCpu = proc.TotalProcessorTime;
                    if (CpuSnapshots.TryGetValue(proc.Id, out var prev))
                    {
                        double elapsedMs = (now - prev.Time).TotalMilliseconds;
                        if (elapsedMs > 50)
                        {
                            double cpuDeltaMs = (totalCpu - prev.Cpu).TotalMilliseconds;
                            cpuPct = Math.Round(Math.Clamp((cpuDeltaMs / (elapsedMs * cores)) * 100.0, 0, 100), 1);
                        }
                    }
                    CpuSnapshots[proc.Id] = (totalCpu, now);
                }
                catch { }

                string status = FrozenPids.Contains(proc.Id)
                    ? "Заморожен"
                    : CriticalSystemProcesses.Contains(proc.ProcessName) ? "Система" : "Активен";

                result.Add(new ProcessInfoItem
                {
                    Pid = proc.Id,
                    Name = proc.ProcessName,
                    RamMb = ramMb,
                    CpuPercent = cpuPct,
                    Status = status
                });
            }
            catch { }
            finally { proc.Dispose(); }
        }

        return result;
    }

    public static (bool Ok, string Reason) FreezeByPid(int pid, string name)
    {
        if (pid <= 4 || CriticalSystemProcesses.Contains(name))
            return (false, $"Нельзя заморозить системный процесс: {name}");

        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero)
            return (false, $"Нет доступа к {name} (PID {pid}) — защищён Windows");

        try
        {
            int ntStatus = NtSuspendProcess(handle);
            if (ntStatus == 0)
            {
                FrozenPids.Add(pid);
                return (true, $"Заморожен: {name} (PID {pid})");
            }
            return (false, $"Windows отклонила заморозку {name} (код 0x{ntStatus:X})");
        }
        finally { CloseHandle(handle); }
    }

    public static bool UnfreezeByPid(int pid)
    {
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero) return false;
        try
        {
            if (NtResumeProcess(handle) == 0)
            {
                FrozenPids.Remove(pid);
                return true;
            }
        }
        finally { CloseHandle(handle); }
        return false;
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

                if (FreezeByPid(proc.Id, name).Ok && !newlyFrozen.Contains(name))
                    newlyFrozen.Add(name);
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
                if (UnfreezeByPid(pid))
                    resumed++;
            }
            catch { }
        }
        FrozenPids.Clear();
        return resumed;
    }
}

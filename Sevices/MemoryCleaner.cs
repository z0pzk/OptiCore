using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OptiCore.Services;

public record RamStats(ulong TotalMb, ulong UsedMb, ulong FreeMb, int UsedPercent);

public static class MemoryCleaner
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_CACHE_INFORMATION_64
    {
        public long CurrentSize;
        public long PeakSize;
        public long PageFaultCount;
        public long MinimumWorkingSet;
        public long MaximumWorkingSet;
        public long CurrentSizeIncludingTransitionInPages;
        public long PeakSizeIncludingTransitionInPages;
        public long TransitionRePurposeCount;
        public long Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int SystemFileCacheInformation = 21;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;
    private const int MemoryFlushModifiedList = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static RamStats GetRamStats()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref status);

        ulong totalMb = status.ullTotalPhys / (1024 * 1024);
        ulong freeMb = status.ullAvailPhys / (1024 * 1024);
        ulong usedMb = totalMb > freeMb ? totalMb - freeMb : 0;
        return new RamStats(totalMb, usedMb, freeMb, (int)status.dwMemoryLoad);
    }

    private static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
            return;

        try
        {
            if (LookupPrivilegeValue(null, privilegeName, out LUID luid))
            {
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static long CleanMemory()
    {
        var before = GetRamStats();

        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeProfileSingleProcessPrivilege");

        // 1. Trim Working Sets of running processes
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4) continue;
                EmptyWorkingSet(proc.Handle);
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        // 2. Trim System File Cache
        try
        {
            var cacheInfo = new SYSTEM_CACHE_INFORMATION_64
            {
                MinimumWorkingSet = -1,
                MaximumWorkingSet = -1
            };
            int size = Marshal.SizeOf(cacheInfo);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(cacheInfo, ptr, false);
                NtSetSystemInformation(SystemFileCacheInformation, ptr, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { }

        // 3. Purge Modified & Standby Memory Lists (Mem Reduct 3.x style)
        PurgeMemoryList(MemoryFlushModifiedList);
        PurgeMemoryList(MemoryPurgeStandbyList);
        PurgeMemoryList(MemoryPurgeLowPriorityStandbyList);

        var after = GetRamStats();
        long freed = (long)before.UsedMb - (long)after.UsedMb;
        return freed > 0 ? freed : 0;
    }

    private static void PurgeMemoryList(int command)
    {
        IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(ptr, command);
            NtSetSystemInformation(SystemMemoryListInformation, ptr, sizeof(int));
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

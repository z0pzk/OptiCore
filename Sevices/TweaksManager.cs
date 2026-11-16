using System.Diagnostics;
using Microsoft.Win32;

namespace OptiCore.Services;

public class TweakState
{
    public bool DisableGameDvr { get; set; }
    public bool DisableFullscreenOptimizations { get; set; }
    public bool EnableHags { get; set; }
    public bool GamingSchedulerPriority { get; set; }
    public bool DisablePowerThrottling { get; set; }
    public bool DisableNetworkThrottling { get; set; }
    public bool DisableWindowsSearch { get; set; }
    public bool DisableSysMain { get; set; }
    public bool DisableTelemetryService { get; set; }
    public bool DisableBackgroundApps { get; set; }
    public bool DisableTransparency { get; set; }
    public bool FastMenuDelay { get; set; }
    public bool DisableHibernation { get; set; }
}

public static class TweaksManager
{
    public static TweakState GetCurrentState()
    {
        return new TweakState
        {
            DisableGameDvr = ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1) == 0,
            DisableFullscreenOptimizations = ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 0) == 2,
            EnableHags = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1) == 2,
            GamingSchedulerPriority = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 20) == 0,
            DisablePowerThrottling = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 0) == 1,
            DisableNetworkThrottling = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 10) == unchecked((int)0xFFFFFFFF),
            DisableWindowsSearch = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 2) == 4,
            DisableSysMain = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2) == 4,
            DisableTelemetryService = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2) == 4,
            DisableBackgroundApps = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0) == 1,
            DisableTransparency = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) == 0,
            FastMenuDelay = ReadString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "400") == "0",
            DisableHibernation = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1) == 0
        };
    }

    public static void SetGameDvr(bool disable)
    {
        WriteDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", disable ? 0 : 1);
        WriteDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", disable ? 0 : 1);
    }

    public static void SetFullscreenOptimizations(bool disable)
    {
        WriteDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", disable ? 2 : 0);
    }

    public static void SetHags(bool enable)
    {
        WriteDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", enable ? 2 : 1);
    }

    public static void SetGamingScheduler(bool optimize)
    {
        WriteDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", optimize ? 0 : 20);
        const string gamesKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        WriteDword(Registry.LocalMachine, gamesKey, "GPU Priority", optimize ? 8 : 2);
        WriteDword(Registry.LocalMachine, gamesKey, "Priority", optimize ? 6 : 2);
        WriteString(Registry.LocalMachine, gamesKey, "Scheduling Category", optimize ? "High" : "Medium");
    }

    public static void SetPowerThrottling(bool disable)
    {
        WriteDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", disable ? 1 : 0);
    }

    public static void SetNetworkThrottling(bool disable)
    {
        WriteDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", disable ? unchecked((int)0xFFFFFFFF) : 10);
    }

    public static void SetServiceDisabled(string serviceName, bool disable, string defaultStart = "auto")
    {
        if (disable)
        {
            RunCommand("sc.exe", $"stop \"{serviceName}\"");
            RunCommand("sc.exe", $"config \"{serviceName}\" start= disabled");
        }
        else
        {
            RunCommand("sc.exe", $"config \"{serviceName}\" start= {defaultStart}");
            RunCommand("sc.exe", $"start \"{serviceName}\"");
        }
    }

    public static void SetBackgroundApps(bool disable)
    {
        WriteDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", disable ? 1 : 0);
    }

    public static void SetTransparency(bool disable)
    {
        WriteDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", disable ? 0 : 1);
    }

    public static void SetFastMenuDelay(bool fast)
    {
        WriteString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", fast ? "0" : "400");
    }

    public static void SetHibernation(bool disable)
    {
        RunCommand("powercfg.exe", disable ? "/hibernate off" : "/hibernate on");
    }

    public static void ApplyAllRecommended()
    {
        SetGameDvr(true);
        SetFullscreenOptimizations(true);
        SetHags(true);
        SetGamingScheduler(true);
        SetPowerThrottling(true);
        SetNetworkThrottling(true);
        SetServiceDisabled("WSearch", true);
        SetServiceDisabled("SysMain", true);
        SetServiceDisabled("DiagTrack", true);
        SetBackgroundApps(true);
        SetTransparency(true);
        SetFastMenuDelay(true);
        SetHibernation(true);
    }

    public static void RestoreDefaults()
    {
        SetGameDvr(false);
        SetFullscreenOptimizations(false);
        SetHags(false);
        SetGamingScheduler(false);
        SetPowerThrottling(false);
        SetNetworkThrottling(false);
        SetServiceDisabled("WSearch", false, "delayed-auto");
        SetServiceDisabled("SysMain", false, "auto");
        SetServiceDisabled("DiagTrack", false, "auto");
        SetBackgroundApps(false);
        SetTransparency(false);
        SetFastMenuDelay(false);
        SetHibernation(false);
    }

    private static int ReadDword(RegistryKey root, string subKey, string name, int fallback)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            if (key?.GetValue(name) is int val) return val;
        }
        catch { }
        return fallback;
    }

    private static string ReadString(RegistryKey root, string subKey, string name, string fallback)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            if (key?.GetValue(name) is string val) return val;
        }
        catch { }
        return fallback;
    }

    private static void WriteDword(RegistryKey root, string subKey, string name, int value)
    {
        try
        {
            using var key = root.CreateSubKey(subKey, true);
            key?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void WriteString(RegistryKey root, string subKey, string name, string value)
    {
        try
        {
            using var key = root.CreateSubKey(subKey, true);
            key?.SetValue(name, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static void RunCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
        }
        catch { }
    }
}

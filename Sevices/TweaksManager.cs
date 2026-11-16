using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace OptiCore.Services;

public class TweakState
{
    // Safe gaming & latency
    public bool DisableGameDvr { get; set; }
    public bool DisableFullscreenOptimizations { get; set; }
    public bool EnableHags { get; set; }
    public bool GamingSchedulerPriority { get; set; }
    public bool DisablePowerThrottling { get; set; }
    public bool DisableNetworkThrottling { get; set; }
    public bool DisableNagleAlgorithm { get; set; }
    public bool DisableMouseAcceleration { get; set; }
    public bool FastKeyboardResponse { get; set; }

    // System, disk & UI
    public bool DisableWindowsSearch { get; set; }
    public bool DisableSysMain { get; set; }
    public bool DisableTelemetryService { get; set; }
    public bool DisableBackgroundApps { get; set; }
    public bool DisableTransparency { get; set; }
    public bool FastMenuDelay { get; set; }
    public bool FastAppShutdown { get; set; }

    // Aggressive / Caveat tweaks (not applied automatically)
    public bool DisableHibernation { get; set; }
    public bool DisableClipboardHistory { get; set; }
    public bool DisableErrorReporting { get; set; }
    public bool DisableXboxServices { get; set; }
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
            DisableNagleAlgorithm = IsNagleDisabled(),
            DisableMouseAcceleration = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "1") == "0",
            FastKeyboardResponse = ReadString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay", "1") == "0",

            DisableWindowsSearch = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 2) == 4,
            DisableSysMain = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2) == 4,
            DisableTelemetryService = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2) == 4,
            DisableBackgroundApps = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0) == 1,
            DisableTransparency = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) == 0,
            FastMenuDelay = ReadString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "400") == "0",
            FastAppShutdown = ReadString(Registry.CurrentUser, @"Control Panel\Desktop", "AutoEndTasks", "0") == "1",

            DisableHibernation = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1) == 0,
            DisableClipboardHistory = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Clipboard", "EnableClipboardHistory", 1) == 0,
            DisableErrorReporting = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\WerSvc", "Start", 3) == 4,
            DisableXboxServices = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\XblGameSave", "Start", 3) == 4
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

    public static void SetNagleAlgorithm(bool disable)
    {
        try
        {
            const string baseKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using var interfaces = Registry.LocalMachine.OpenSubKey(baseKey, true);
            if (interfaces == null) return;
            foreach (string sub in interfaces.GetSubKeyNames())
            {
                using var nic = interfaces.OpenSubKey(sub, true);
                if (nic == null) continue;
                if (disable)
                {
                    nic.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    nic.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                }
                else
                {
                    nic.DeleteValue("TcpAckFrequency", false);
                    nic.DeleteValue("TCPNoDelay", false);
                }
            }
        }
        catch { }
    }

    private static bool IsNagleDisabled()
    {
        try
        {
            const string baseKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
            using var interfaces = Registry.LocalMachine.OpenSubKey(baseKey);
            if (interfaces == null) return false;
            foreach (string sub in interfaces.GetSubKeyNames())
            {
                using var nic = interfaces.OpenSubKey(sub);
                if (nic?.GetValue("TCPNoDelay") is int val && val == 1)
                    return true;
            }
        }
        catch { }
        return false;
    }

    public static void SetMouseAcceleration(bool disable)
    {
        WriteString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", disable ? "0" : "1");
        WriteString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", disable ? "0" : "6");
        WriteString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", disable ? "0" : "10");
    }

    public static void SetFastKeyboard(bool fast)
    {
        WriteString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay", fast ? "0" : "1");
        WriteString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed", fast ? "31" : "31");
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

    public static void SetFastAppShutdown(bool fast)
    {
        WriteString(Registry.CurrentUser, @"Control Panel\Desktop", "AutoEndTasks", fast ? "1" : "0");
        WriteString(Registry.CurrentUser, @"Control Panel\Desktop", "WaitToKillAppTimeout", fast ? "2000" : "20000");
        WriteString(Registry.CurrentUser, @"Control Panel\Desktop", "HungAppTimeout", fast ? "2000" : "5000");
    }

    public static void SetHibernation(bool disable)
    {
        RunCommand("powercfg.exe", disable ? "/hibernate off" : "/hibernate on");
    }

    public static void SetClipboardHistory(bool disable)
    {
        WriteDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Clipboard", "EnableClipboardHistory", disable ? 0 : 1);
    }

    public static void SetXboxServices(bool disable)
    {
        string[] xboxSvcs = { "XblAuthManager", "XblGameSave", "XboxNetApiSvc" };
        foreach (string svc in xboxSvcs)
            SetServiceDisabled(svc, disable, "demand");
    }

    public static void ApplyAllRecommended()
    {
        // Applies ONLY safe & moderate tweaks; leaves [Осторожно] tweaks untouched
        SetGameDvr(true);
        SetFullscreenOptimizations(true);
        SetHags(true);
        SetGamingScheduler(true);
        SetPowerThrottling(true);
        SetNetworkThrottling(true);
        SetNagleAlgorithm(true);
        SetMouseAcceleration(true);
        SetFastKeyboard(true);
        SetServiceDisabled("WSearch", true);
        SetServiceDisabled("SysMain", true);
        SetServiceDisabled("DiagTrack", true);
        SetBackgroundApps(true);
        SetTransparency(true);
        SetFastMenuDelay(true);
        SetFastAppShutdown(true);
    }

    public static void RestoreDefaults()
    {
        SetGameDvr(false);
        SetFullscreenOptimizations(false);
        SetHags(false);
        SetGamingScheduler(false);
        SetPowerThrottling(false);
        SetNetworkThrottling(false);
        SetNagleAlgorithm(false);
        SetMouseAcceleration(false);
        SetFastKeyboard(false);
        SetServiceDisabled("WSearch", false, "delayed-auto");
        SetServiceDisabled("SysMain", false, "auto");
        SetServiceDisabled("DiagTrack", false, "auto");
        SetBackgroundApps(false);
        SetTransparency(false);
        SetFastMenuDelay(false);
        SetFastAppShutdown(false);
        SetHibernation(false);
        SetClipboardHistory(false);
        SetServiceDisabled("WerSvc", false, "demand");
        SetXboxServices(false);
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

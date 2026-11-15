using System.Diagnostics;

namespace OptiCore.Services;

public static class PowerManager
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // Processor power management subgroup & settings (Core Parking + Min Frequency)
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CoreParkingMin = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";

    public static bool IsHighPerformanceActive { get; private set; }

    public static void SetHighPerformanceMode()
    {
        // Try switching to High Performance plan (if available) and unpark 100% of CPU cores
        RunPowerCfg($"/setactive {HighPerfGuid}");
        RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {CoreParkingMin} 100");
        RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMin} 100");
        RunPowerCfg("/setactive SCHEME_CURRENT");
        IsHighPerformanceActive = true;
    }

    public static void SetBalancedMode()
    {
        RunPowerCfg($"/setactive {BalancedGuid}");
        RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {CoreParkingMin} 10");
        RunPowerCfg($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {ProcThrottleMin} 5");
        RunPowerCfg("/setactive SCHEME_CURRENT");
        IsHighPerformanceActive = false;
    }

    private static void RunPowerCfg(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1500);
        }
        catch { }
    }
}

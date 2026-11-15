using System.Diagnostics;

namespace OptiCore.Services;

public static class StartupManager
{
    private const string TaskName = "OptiCoreOptimizer";

    public static bool IsEnabled()
    {
        return RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;
    }

    public static bool SetEnabled(bool enable)
    {
        if (enable)
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;
            string args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F";
            return RunSchtasks(args) == 0;
        }
        else
        {
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }
}

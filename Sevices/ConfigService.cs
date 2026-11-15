using System.IO;
using System.Text.Json;

namespace OptiCore.Services;

public class AppConfig
{
    public bool AutoCleanEnabled { get; set; } = true;
    public int AutoCleanThresholdPercent { get; set; } = 80;
    public bool AutoGameModeEnabled { get; set; } = true;
    public bool AutoFreezeOnGame { get; set; } = true;
    public bool AutoPowerOnGame { get; set; } = true;
    public bool HighPriorityOnGame { get; set; } = true;
    public bool TimerResolutionOnGame { get; set; } = true;

    public List<string> Games { get; set; } = new()
    {
        "RobloxPlayerBeta",
        "cs2",
        "dota2",
        "javaw",
        "Minecraft.Windows",
        "GTA5",
        "DOOMEternalx64vk"
    };

    public List<string> FreezeTargets { get; set; } = new()
    {
        "chrome",
        "msedge",
        "opera",
        "OneDrive",
        "EpicGamesLauncher",
        "Spotify",
        "wallpaper32",
        "wallpaper64"
    };

    public List<string> Whitelist { get; set; } = new()
    {
        "Discord",
        "Telegram",
        "steam",
        "steamwebhelper",
        "obs64",
        "v2rayN"
    };
}

public static class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "opticore_config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
        }
        catch { }
    }
}

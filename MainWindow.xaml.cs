using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using OptiCore.Services;

namespace OptiCore;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly NotifyIcon _trayIcon;
    private DateTime _lastAutoClean = DateTime.MinValue;
    private bool _isGameModeActive = false;
    private bool _isLoaded = false;
    private bool _reallyExit = false;

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigService.Load();

        ChkAutoClean.IsChecked = _config.AutoCleanEnabled;
        SliderThreshold.Value = _config.AutoCleanThresholdPercent;
        TxtThreshold.Text = $"{_config.AutoCleanThresholdPercent}%";
        ChkAutoGameMode.IsChecked = _config.AutoGameModeEnabled;

        RefreshLists();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "OptiCore — Системный оптимизатор",
            Visible = true
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreWindow());
        menu.Items.Add("Очистить RAM", null, (_, _) => CleanRam());
        menu.Items.Add("Разморозить всё и выйти", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _isLoaded = true;
        UpdateRamUi();
        Log("OptiCore запущен.");
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var stats = UpdateRamUi();

        // 1. Auto-clean RAM by threshold (cooldown 30s to prevent stutter loops)
        if (_config.AutoCleanEnabled &&
            stats.UsedPercent >= _config.AutoCleanThresholdPercent &&
            (DateTime.Now - _lastAutoClean).TotalSeconds >= 30)
        {
            _lastAutoClean = DateTime.Now;
            long freed = MemoryCleaner.CleanMemory();
            Log($"Авто-очистка RAM (порог {_config.AutoCleanThresholdPercent}%): освобождено {freed} МБ");
            UpdateRamUi();
        }

        // 2. Auto Game Mode (CPU Power + Process Freezer)
        if (_config.AutoGameModeEnabled)
        {
            string? runningGame = ProcessFreezer.GetActiveGame(_config.Games);
            if (runningGame != null && !_isGameModeActive)
            {
                ActivateGameMode(runningGame);
            }
            else if (runningGame == null && _isGameModeActive)
            {
                DeactivateGameMode();
            }
        }
    }

    private RamStats UpdateRamUi()
    {
        var stats = MemoryCleaner.GetRamStats();
        TxtRamHeader.Text = $"RAM: {stats.UsedPercent}% ({stats.UsedMb} / {stats.TotalMb} МБ)";
        RamProgress.Value = stats.UsedPercent;
        return stats;
    }

    private void CleanRam()
    {
        long freed = MemoryCleaner.CleanMemory();
        UpdateRamUi();
        Log($"Ручная очистка RAM: освобождено {freed} МБ");
    }

    private void ActivateGameMode(string gameName)
    {
        _isGameModeActive = true;
        TxtGameStatus.Text = $"Активна игра: {gameName} (игровой режим включён)";
        TxtGameStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));

        if (_config.AutoPowerOnGame)
        {
            PowerManager.SetHighPerformanceMode();
            TxtPowerMode.Text = "CPU: Игровой (ядра 100%)";
            BadgePower.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
        }

        if (_config.AutoFreezeOnGame)
        {
            var frozen = ProcessFreezer.FreezeTargets(_config.FreezeTargets, _config.Whitelist, _config.Games);
            if (frozen.Count > 0)
                Log($"Заморожены процессы: {string.Join(", ", frozen)}");
        }

        Log($"Включён игровой режим ({gameName}).");
    }

    private void DeactivateGameMode()
    {
        _isGameModeActive = false;
        TxtGameStatus.Text = "Игра не обнаружена — режим ожидания";
        TxtGameStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));

        PowerManager.SetBalancedMode();
        TxtPowerMode.Text = "CPU: Средний режим";
        BadgePower.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));

        int resumed = ProcessFreezer.UnfreezeAll();
        if (resumed > 0)
            Log($"Разморожено процессов: {resumed}");

        Log("Возврат в средний режим.");
    }

    private void RefreshLists()
    {
        LstGames.ItemsSource = null;
        LstGames.ItemsSource = _config.Games;
        LstFreeze.ItemsSource = null;
        LstFreeze.ItemsSource = _config.FreezeTargets;
        LstWhitelist.ItemsSource = null;
        LstWhitelist.ItemsSource = _config.Whitelist;
    }

    private void Log(string message)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LstLog.Items.Insert(0, entry);
        if (LstLog.Items.Count > 100)
            LstLog.Items.RemoveAt(LstLog.Items.Count - 1);
    }

    private void BtnCleanRam_Click(object sender, RoutedEventArgs e) => CleanRam();
    private void BtnGameModeOn_Click(object sender, RoutedEventArgs e) => ActivateGameMode("Ручной режим");
    private void BtnGameModeOff_Click(object sender, RoutedEventArgs e) => DeactivateGameMode();

    private void BtnFreezeNow_Click(object sender, RoutedEventArgs e)
    {
        var frozen = ProcessFreezer.FreezeTargets(_config.FreezeTargets, _config.Whitelist, _config.Games);
        Log(frozen.Count > 0
            ? $"Заморожены процессы: {string.Join(", ", frozen)}"
            : "Нет подходящих процессов для заморозки.");
    }

    private void BtnUnfreezeAll_Click(object sender, RoutedEventArgs e)
    {
        int count = ProcessFreezer.UnfreezeAll();
        Log($"Разморожено процессов: {count}");
    }

    private void ConfigChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _config.AutoCleanEnabled = ChkAutoClean.IsChecked == true;
        _config.AutoGameModeEnabled = ChkAutoGameMode.IsChecked == true;
        ConfigService.Save(_config);
    }

    private void SliderThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded) return;
        int val = (int)SliderThreshold.Value;
        TxtThreshold.Text = $"{val}%";
        _config.AutoCleanThresholdPercent = val;
        ConfigService.Save(_config);
    }

    private static string NormalizeProcName(string input) =>
        input.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

    private void BtnAddGame_Click(object sender, RoutedEventArgs e)
    {
        string name = NormalizeProcName(TxtNewGame.Text);
        if (!string.IsNullOrEmpty(name) && !_config.Games.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _config.Games.Add(name);
            TxtNewGame.Clear();
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void BtnDelGame_Click(object sender, RoutedEventArgs e)
    {
        if (LstGames.SelectedItem is string item)
        {
            _config.Games.Remove(item);
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void BtnAddFreeze_Click(object sender, RoutedEventArgs e)
    {
        string name = NormalizeProcName(TxtNewFreeze.Text);
        if (!string.IsNullOrEmpty(name) && !_config.FreezeTargets.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _config.FreezeTargets.Add(name);
            TxtNewFreeze.Clear();
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void BtnDelFreeze_Click(object sender, RoutedEventArgs e)
    {
        if (LstFreeze.SelectedItem is string item)
        {
            _config.FreezeTargets.Remove(item);
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void BtnAddWhite_Click(object sender, RoutedEventArgs e)
    {
        string name = NormalizeProcName(TxtNewWhite.Text);
        if (!string.IsNullOrEmpty(name) && !_config.Whitelist.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _config.Whitelist.Add(name);
            TxtNewWhite.Clear();
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void BtnDelWhite_Click(object sender, RoutedEventArgs e)
    {
        if (LstWhitelist.SelectedItem is string item)
        {
            _config.Whitelist.Remove(item);
            RefreshLists();
            ConfigService.Save(_config);
        }
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        ProcessFreezer.UnfreezeAll();
        PowerManager.SetBalancedMode();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
        }
    }
}

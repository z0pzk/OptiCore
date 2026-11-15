using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
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
    private string? _activeGameName = null;
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
        ChkHighPriority.IsChecked = _config.HighPriorityOnGame;
        ChkTimerRes.IsChecked = _config.TimerResolutionOnGame;
        ChkAutostart.IsChecked = StartupManager.IsEnabled();

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
        menu.Items.Add("Игровой режим Вкл", null, (_, _) => ActivateGameMode("Ручной режим"));
        menu.Items.Add("Средний режим", null, (_, _) => DeactivateGameMode());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Разморозить всё и выйти", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _isLoaded = true;
        UpdateRamUi();
        Log("OptiCore запущен.");

        if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Minimized;
            Hide();
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var stats = UpdateRamUi();

        if (_config.AutoCleanEnabled &&
            stats.UsedPercent >= _config.AutoCleanThresholdPercent &&
            (DateTime.Now - _lastAutoClean).TotalSeconds >= 30)
        {
            _lastAutoClean = DateTime.Now;
            long freed = MemoryCleaner.CleanMemory();
            Log($"Авто-очистка RAM (порог {_config.AutoCleanThresholdPercent}%): освобождено {freed} МБ");
            UpdateRamUi();
        }

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

        if (MainTabs.SelectedIndex == 1 && IsVisible)
        {
            RefreshTaskManager();
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
        _activeGameName = gameName;
        TxtGameStatus.Text = $"Активна игра: {gameName} (игровой режим включён)";
        TxtGameStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));

        if (_config.AutoPowerOnGame)
        {
            PowerManager.SetHighPerformanceMode(_config.TimerResolutionOnGame);
            TxtPowerMode.Text = _config.TimerResolutionOnGame
                ? "CPU: Игровой (ядра 100%, 0.5 мс)"
                : "CPU: Игровой (ядра 100%)";
            BadgePower.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
        }

        if (_config.HighPriorityOnGame && gameName != "Ручной режим")
        {
            ProcessFreezer.SetGameHighPriority(gameName, true);
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
        if (_activeGameName != null && _activeGameName != "Ручной режим")
            ProcessFreezer.SetGameHighPriority(_activeGameName, false);

        _isGameModeActive = false;
        _activeGameName = null;
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

    private void RefreshTaskManager()
    {
        int? selectedPid = (GridProcesses.SelectedItem as ProcessInfoItem)?.Pid;
        var list = ProcessFreezer.GetRunningProcesses();

        IEnumerable<ProcessInfoItem> sorted = CmbSort.SelectedIndex switch
        {
            0 => list.OrderByDescending(p => p.RamMb),
            1 => list.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.RamMb),
            _ => list.OrderBy(p => p.Name)
        };

        var finalItems = sorted.ToList();
        GridProcesses.ItemsSource = finalItems;

        if (selectedPid.HasValue)
        {
            var match = finalItems.FirstOrDefault(x => x.Pid == selectedPid.Value);
            if (match != null) GridProcesses.SelectedItem = match;
        }
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

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded && MainTabs.SelectedIndex == 1)
            RefreshTaskManager();
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded) RefreshTaskManager();
    }

    private void BtnRefreshProcs_Click(object sender, RoutedEventArgs e) => RefreshTaskManager();

    private void BtnProcFreeze_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item)
        {
            if (ProcessFreezer.FreezeByPid(item.Pid, item.Name))
                Log($"Заморожен: {item.Name} (PID {item.Pid})");
            else
                Log($"Нельзя заморозить системный процесс: {item.Name}");
            RefreshTaskManager();
        }
    }

    private void BtnProcUnfreeze_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item)
        {
            if (ProcessFreezer.UnfreezeByPid(item.Pid))
                Log($"Разморожен: {item.Name} (PID {item.Pid})");
            RefreshTaskManager();
        }
    }

    private void BtnProcAddFreeze_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item &&
            !_config.FreezeTargets.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _config.FreezeTargets.Add(item.Name);
            RefreshLists();
            ConfigService.Save(_config);
            Log($"Добавлен в заморозку фона: {item.Name}");
        }
    }

    private void BtnProcAddWhite_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item &&
            !_config.Whitelist.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _config.Whitelist.Add(item.Name);
            RefreshLists();
            ConfigService.Save(_config);
            Log($"Добавлен в белый список: {item.Name}");
        }
    }

    private void BtnProcAddGame_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item &&
            !_config.Games.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
        {
            _config.Games.Add(item.Name);
            RefreshLists();
            ConfigService.Save(_config);
            Log($"Добавлен в список игр: {item.Name}");
        }
    }

    private void BtnProcKill_Click(object sender, RoutedEventArgs e)
    {
        if (GridProcesses.SelectedItem is ProcessInfoItem item)
        {
            if (ProcessFreezer.IsProtected(item.Name))
            {
                Log($"Защита: нельзя завершать системный процесс {item.Name}");
                return;
            }
            try
            {
                Process.GetProcessById(item.Pid).Kill();
                Log($"Завершён процесс: {item.Name} (PID {item.Pid})");
                RefreshTaskManager();
            }
            catch (Exception ex)
            {
                Log($"Ошибка завершения {item.Name}: {ex.Message}");
            }
        }
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

    private void ChkAutostart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        bool enable = ChkAutostart.IsChecked == true;
        bool ok = StartupManager.SetEnabled(enable);
        Log(ok
            ? (enable ? "Автозагрузка с Windows включена." : "Автозагрузка отключена.")
            : "Не удалось изменить автозагрузку.");
    }

    private void ConfigChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _config.AutoCleanEnabled = ChkAutoClean.IsChecked == true;
        _config.AutoGameModeEnabled = ChkAutoGameMode.IsChecked == true;
        _config.HighPriorityOnGame = ChkHighPriority.IsChecked == true;
        _config.TimerResolutionOnGame = ChkTimerRes.IsChecked == true;
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

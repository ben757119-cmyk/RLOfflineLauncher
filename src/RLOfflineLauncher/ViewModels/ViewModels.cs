using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using RLOfflineLauncher.Models;
using RLOfflineLauncher.Services;

namespace RLOfflineLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedTabIndex;

    public LauncherViewModel Launcher { get; }
    public CosmeticsViewModel Cosmetics { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }

    public MainViewModel(
        LauncherViewModel launcher,
        CosmeticsViewModel cosmetics,
        SettingsViewModel settings,
        LogsViewModel logs)
    {
        Launcher = launcher;
        Cosmetics = cosmetics;
        Settings = settings;
        Logs = logs;
    }

    [RelayCommand]
    private void SelectTab(object? parameter)
    {
        if (parameter is string s && int.TryParse(s, out int idx))
            SelectedTabIndex = idx;
        else if (parameter is int i)
            SelectedTabIndex = i;
    }
}

public partial class LauncherViewModel : ObservableObject
{
    private readonly IEacDetectionService _eac;
    private readonly ILaunchPlanner _planner;
    private readonly IGameLauncher _launcher;
    private readonly IDialogService _dialog;
    private readonly IUrlOpener _url;
    private readonly ICosmeticApplier _applier;
    private readonly ISettingsService _settings;
    private readonly ILogBuffer _log;
    private readonly ILogger<LauncherViewModel> _logger;

    [ObservableProperty] private string _gamePath = string.Empty;
    [ObservableProperty] private string _commandLinePreview = string.Empty;
    [ObservableProperty] private string _eacStatusText = "Not scanned";
    [ObservableProperty] private string _eacStatusColor = "Gray";
    [ObservableProperty] private string _launchStateText = "Idle";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private EacScanResult? _lastScan;

    public LauncherViewModel(
        IEacDetectionService eac,
        ILaunchPlanner planner,
        IGameLauncher launcher,
        IDialogService dialog,
        IUrlOpener url,
        ICosmeticApplier applier,
        ISettingsService settings,
        ILogBuffer log,
        ILogger<LauncherViewModel> logger)
    {
        _eac = eac;
        _planner = planner;
        _launcher = launcher;
        _dialog = dialog;
        _url = url;
        _applier = applier;
        _settings = settings;
        _log = log;
        _logger = logger;

        var s = _settings.Load();
        GamePath = s.GamePath;
        UpdateCommandLinePreview();
    }

    partial void OnGamePathChanged(string value) => UpdateCommandLinePreview();

    private void UpdateCommandLinePreview()
    {
        var s = _settings.Load();
        var plan = _planner.CreatePlan(GamePath, s.UseOfflineFlag, s.UseNoAntiCheatFlag);
        CommandLinePreview = plan.IsSupported
            ? $"{plan.ExecutablePath} {string.Join(' ', plan.Arguments)}"
            : "(invalid path or unsupported mechanism)";
    }

    [RelayCommand]
    private void BrowsePath()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Rocket League executable"
        };
        if (dlg.ShowDialog() == true)
        {
            GamePath = dlg.FileName;
            var s = _settings.Load();
            s.GamePath = GamePath;
            _settings.Save(s);
            _log.Add("Game path set to " + GamePath);
        }
    }

    [RelayCommand]
    private async Task ScanEacAsync()
    {
        IsBusy = true;
        LaunchStateText = "Scanning\u2026";
        try
        {
            await Task.Run(() => { LastScan = _eac.Scan(); });
            ApplyScanUi(LastScan!);
            _log.Add("EAC scan: " + LastScan!.Summary);
        }
        finally
        {
            IsBusy = false;
            if (LastScan?.State == EacDetectionState.NotDetected)
                LaunchStateText = "Idle (clear)";
            else if (LastScan?.State == EacDetectionState.Detected)
                LaunchStateText = "Blocked (EAC detected)";
            else
                LaunchStateText = "Blocked (scan inconclusive)";
        }
    }

    private void ApplyScanUi(EacScanResult r)
    {
        EacStatusText = r.Summary;
        EacStatusColor = r.State switch
        {
            EacDetectionState.NotDetected => "LimeGreen",
            EacDetectionState.Detected => "OrangeRed",
            EacDetectionState.Inconclusive => "Gold",
            _ => "Gray"
        };
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        LaunchStateText = "Scanning\u2026";

        try
        {
            var scan = await Task.Run(() => _eac.Scan());
            LastScan = scan;
            ApplyScanUi(scan);

            if (scan.State == EacDetectionState.Detected)
            {
                LaunchStateText = "Blocked (EAC detected)";
                _log.Add("Launch aborted \u2013 EAC detected");

                const string exactMessage =
                    "Please launch Rocket League without Easy Anti-Cheat\n\nDo you need help?";

                bool yes = _dialog.ShowYesNo("Easy Anti-Cheat Detected", exactMessage);
                if (yes)
                {
                    _url.Open("https://youtube.com");
                    _log.Add("Opened https://youtube.com for help");
                }
                return;
            }

            if (scan.State == EacDetectionState.Inconclusive)
            {
                LaunchStateText = "Blocked (scan inconclusive)";
                _dialog.ShowWarning("Safety check incomplete",
                    "The EAC safety check could not be completed reliably. Launch has been blocked.\n\nYou can retry the scan from the Launcher page.");
                _log.Add("Launch aborted \u2013 scan inconclusive");
                return;
            }

            var settings = _settings.Load();
            var plan = _planner.CreatePlan(GamePath, settings.UseOfflineFlag, settings.UseNoAntiCheatFlag);
            if (!plan.IsSupported)
            {
                LaunchStateText = "Blocked (unsupported mechanism)";
                _dialog.ShowWarning("Cannot launch", plan.CapabilityNote);
                return;
            }

            LaunchStateText = "Launching\u2026";
            var (ok, message, pid) = await _launcher.LaunchAsync(plan);
            if (ok)
            {
                LaunchStateText = "Process started";
                _log.Add(message);
                await _applier.InitializeAsync();
            }
            else
            {
                LaunchStateText = "Failed";
                _dialog.ShowWarning("Launch failed", message);
                _log.Add("Launch failed: " + message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class CosmeticsViewModel : ObservableObject
{
    private readonly ICosmeticCatalogService _catalog;
    private readonly ICosmeticApplier _applier;
    private readonly ILogBuffer _log;

    [ObservableProperty] private ObservableCollection<CosmeticItem> _items = new();
    [ObservableProperty] private CosmeticItem? _selectedItem;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All";
    [ObservableProperty] private string _statusText = "Preview only (no game integration)";
    [ObservableProperty] private string _bannerText =
        "Selecting an item does not grant ownership. Changes are temporary, local and visual only.";

    public ObservableCollection<string> Categories { get; } = new()
    {
        "All", "Cars/Body styles", "Titles", "Banners", "Decals", "Wheels",
        "Goal Explosions", "Trails", "Other"
    };

    public CosmeticsViewModel(ICosmeticCatalogService catalog, ICosmeticApplier applier, ILogBuffer log)
    {
        _catalog = catalog;
        _applier = applier;
        _log = log;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedCategoryChanged(string value) => Refresh();

    private void Refresh()
    {
        try
        {
            var list = _catalog.Filter(SearchText, SelectedCategory, null);
            Items = new ObservableCollection<CosmeticItem>(list);
        }
        catch (Exception ex)
        {
            _log.Add("Catalog error: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedItem is null) return;
        var result = await _applier.ApplyAsync(SelectedItem);
        StatusText = result.Message;
        _log.Add(result.Message);
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        var result = await _applier.ResetAsync(SelectedItem);
        StatusText = result.Message;
        _log.Add(result.Message);
    }

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        var result = await _applier.ResetAsync(null);
        StatusText = result.Message;
        _log.Add(result.Message);
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILogBuffer _log;

    [ObservableProperty] private string _gamePath = string.Empty;
    [ObservableProperty] private bool _useOfflineFlag = true;
    [ObservableProperty] private bool _useNoAntiCheatFlag = true;
    [ObservableProperty] private string _hotkeyModifier = "F3";
    [ObservableProperty] private string _hotkeyKey = "T";
    [ObservableProperty] private bool _allowHotkeyWhenFocused;
    [ObservableProperty] private string _logLevel = "Information";

    public SettingsViewModel(ISettingsService settings, ILogBuffer log)
    {
        _settings = settings;
        _log = log;
        Load();
    }

    private void Load()
    {
        var s = _settings.Load();
        GamePath = s.GamePath;
        UseOfflineFlag = s.UseOfflineFlag;
        UseNoAntiCheatFlag = s.UseNoAntiCheatFlag;
        HotkeyModifier = s.HotkeyModifier;
        HotkeyKey = s.HotkeyKey;
        AllowHotkeyWhenFocused = s.AllowHotkeyWhenLauncherFocused;
        LogLevel = s.LogLevel;
    }

    [RelayCommand]
    private void Save()
    {
        var s = new AppSettings
        {
            GamePath = GamePath,
            UseOfflineFlag = UseOfflineFlag,
            UseNoAntiCheatFlag = UseNoAntiCheatFlag,
            HotkeyModifier = HotkeyModifier,
            HotkeyKey = HotkeyKey,
            AllowHotkeyWhenLauncherFocused = AllowHotkeyWhenFocused,
            LogLevel = LogLevel
        };
        _settings.Save(s);
        _log.Add("Settings saved");
        MessageBox.Show("Settings saved.", "RL Offline Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void Reset()
    {
        _settings.Save(new AppSettings());
        Load();
        _log.Add("Settings reset to defaults");
    }
}

public partial class LogsViewModel : ObservableObject
{
    private readonly ILogBuffer _buffer;
    private readonly ISettingsService _settings;

    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private string _diagnosticSummary = string.Empty;

    public LogsViewModel(ILogBuffer buffer, ISettingsService settings)
    {
        _buffer = buffer;
        _settings = settings;
        _buffer.Changed += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        LogText = string.Join(Environment.NewLine, _buffer.Entries);
        var s = _settings.Load();
        DiagnosticSummary =
            $"OS: {Environment.OSVersion}\n" +
            $".NET: {Environment.Version}\n" +
            $"App: 1.0.0\n" +
            $"Game path: {(string.IsNullOrEmpty(s.GamePath) ? "(not set)" : s.GamePath)}";
    }

    [RelayCommand]
    private void Copy()
    {
        try { Clipboard.SetText(LogText); } catch { }
    }

    [RelayCommand]
    private void Clear()
    {
        _buffer.Clear();
    }

    [RelayCommand]
    private void Export()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            FileName = $"rl-offline-launcher-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, LogText + Environment.NewLine + DiagnosticSummary);
        }
    }
}

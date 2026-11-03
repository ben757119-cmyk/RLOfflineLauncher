using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RLOfflineLauncher.Models;

namespace RLOfflineLauncher.Services;

public interface IProcessProvider
{
    IReadOnlyList<ProcessInfo> GetProcesses();
}

public sealed class ProcessInfo
{
    public int Id { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? MainModulePath { get; init; }
    public bool AccessDenied { get; init; }
}

public sealed class ProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                list.Add(new ProcessInfo
                {
                    Id = p.Id,
                    ProcessName = p.ProcessName,
                    MainModulePath = path,
                    AccessDenied = false
                });
            }
            catch (Exception)
            {
                list.Add(new ProcessInfo
                {
                    Id = p.Id,
                    ProcessName = p.ProcessName,
                    AccessDenied = true
                });
            }
            finally
            {
                p.Dispose();
            }
        }
        return list;
    }
}

public interface IEacDetectionService
{
    EacScanResult Scan();
}

public sealed class EacDetectionService : IEacDetectionService
{
    public static readonly string[] KnownEacProcessNames =
    {
        "EasyAntiCheat",
        "EasyAntiCheat_EOS",
        "EasyAntiCheat_Launcher",
        "EasyAntiCheat_Setup",
        "EasyAntiCheat_EOS_Setup"
    };

    public const double InaccessibleThreshold = 0.40;

    private readonly IProcessProvider _provider;
    private readonly ILogger<EacDetectionService> _logger;

    public EacDetectionService(IProcessProvider provider, ILogger<EacDetectionService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public EacScanResult Scan()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var matchedNames = new List<string>();
        var matchedIds = new List<int>();
        var errors = new List<string>();
        int scanned = 0;
        int inaccessible = 0;

        IReadOnlyList<ProcessInfo> processes;
        try
        {
            processes = _provider.GetProcesses();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to obtain process list");
            return new EacScanResult
            {
                State = EacDetectionState.Inconclusive,
                Timestamp = timestamp,
                Errors = new[] { "Could not enumerate processes: " + ex.Message }
            };
        }

        foreach (var p in processes)
        {
            scanned++;
            if (p.AccessDenied)
            {
                inaccessible++;
                continue;
            }

            var name = p.ProcessName;
            var nameWithoutExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            foreach (var known in KnownEacProcessNames)
            {
                if (string.Equals(nameWithoutExe, known, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, known, StringComparison.OrdinalIgnoreCase) ||
                    (p.MainModulePath != null &&
                     p.MainModulePath.Contains(known, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedNames.Add(p.ProcessName);
                    matchedIds.Add(p.Id);
                    break;
                }
            }
        }

        EacDetectionState state;
        if (matchedNames.Count > 0)
        {
            state = EacDetectionState.Detected;
        }
        else if (scanned == 0 || (double)inaccessible / scanned > InaccessibleThreshold)
        {
            state = EacDetectionState.Inconclusive;
            errors.Add($"Too many inaccessible processes ({inaccessible}/{scanned}). Threshold = {InaccessibleThreshold:P0}");
        }
        else
        {
            state = EacDetectionState.NotDetected;
        }

        _logger.LogInformation("EAC scan finished: {State}, scanned={Scanned}, inaccessible={Inaccessible}, matches={Matches}",
            state, scanned, inaccessible, matchedNames.Count);

        return new EacScanResult
        {
            State = state,
            Timestamp = timestamp,
            ProcessesScanned = scanned,
            ProcessesInaccessible = inaccessible,
            MatchedProcessNames = matchedNames,
            MatchedProcessIds = matchedIds,
            Errors = errors
        };
    }
}

public interface IDialogService
{
    bool ShowYesNo(string title, string message);
    void ShowWarning(string title, string message);
    void ShowInfo(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public bool ShowYesNo(string title, string message)
    {
        var result = System.Windows.MessageBox.Show(
            message, title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    public void ShowWarning(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title,
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    public void ShowInfo(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title,
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }
}

public interface IUrlOpener
{
    void Open(string url);
}

public sealed class UrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}

public static class LaunchFlags
{
    public const string Offline = "-offline";
    public const string NoAntiCheat = "-noanticheat";
}

public interface ILaunchPlanner
{
    LaunchPlan CreatePlan(string gamePath, bool useOffline, bool useNoAntiCheat);
}

public sealed class LaunchPlanner : ILaunchPlanner
{
    public LaunchPlan CreatePlan(string gamePath, bool useOffline, bool useNoAntiCheat)
    {
        if (string.IsNullOrWhiteSpace(gamePath) ||
            !File.Exists(gamePath) ||
            !gamePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchPlan
            {
                Mechanism = LaunchMechanismKind.Unknown,
                CapabilityNote = "Invalid or missing executable path."
            };
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(gamePath);
            var attrs = File.GetAttributes(fullPath);
            _ = attrs; // silence unused
        }
        catch
        {
            return new LaunchPlan
            {
                Mechanism = LaunchMechanismKind.Unknown,
                CapabilityNote = "Could not resolve canonical path."
            };
        }

        var args = new List<string>();
        if (useOffline) args.Add(LaunchFlags.Offline);
        if (useNoAntiCheat) args.Add(LaunchFlags.NoAntiCheat);

        return new LaunchPlan
        {
            Mechanism = LaunchMechanismKind.DirectExecutable,
            ExecutablePath = fullPath,
            Arguments = args,
            ArgumentsSupported = true,
            CapabilityNote =
                "Direct executable launch. Arguments are passed via ProcessStartInfo.ArgumentList. " +
                "Whether Rocket League honors -offline / -noanticheat is unverified and depends on game version. " +
                "Official documented flag for no-EAC is -noeac via the launcher."
        };
    }
}

public interface IGameLauncher
{
    Task<(bool Success, string Message, int? ProcessId)> LaunchAsync(LaunchPlan plan, CancellationToken ct = default);
}

public sealed class GameLauncher : IGameLauncher
{
    private readonly ILogger<GameLauncher> _logger;

    public GameLauncher(ILogger<GameLauncher> logger) => _logger = logger;

    public async Task<(bool Success, string Message, int? ProcessId)> LaunchAsync(LaunchPlan plan, CancellationToken ct = default)
    {
        if (!plan.IsSupported)
            return (false, "Launch mechanism is not supported for argument passing: " + plan.CapabilityNote, null);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(plan.ExecutablePath) ?? string.Empty
            };
            foreach (var a in plan.Arguments)
                psi.ArgumentList.Add(a);

            _logger.LogInformation("Starting process: {Exe} {Args}", plan.ExecutablePath, string.Join(' ', plan.Arguments));

            var process = Process.Start(psi);
            if (process is null)
                return (false, "Process.Start returned null.", null);

            await Task.Delay(800, ct);
            if (process.HasExited)
            {
                return (false, $"Process exited immediately with code {process.ExitCode}.", process.Id);
            }

            return (true,
                $"Process started (PID {process.Id}) with arguments: {string.Join(' ', plan.Arguments)}. " +
                "Rocket League's actual online/offline state is not verified by this tool.",
                process.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch failed");
            return (false, "Launch failed: " + ex.Message, null);
        }
    }
}

public interface IGameProcessMonitor
{
    bool IsGameRunning { get; }
    event EventHandler? GameStarted;
    event EventHandler? GameExited;
    void Start();
    void Stop();
}

public sealed class GameProcessMonitor : IGameProcessMonitor, IDisposable
{
    private readonly ILogger<GameProcessMonitor> _logger;
    private Timer? _timer;
    private bool _wasRunning;

    public bool IsGameRunning { get; private set; }
    public event EventHandler? GameStarted;
    public event EventHandler? GameExited;

    public GameProcessMonitor(ILogger<GameProcessMonitor> logger) => _logger = logger;

    public void Start()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        bool running = false;
        try
        {
            foreach (var p in Process.GetProcessesByName("RocketLeague"))
            {
                running = true;
                p.Dispose();
            }
        }
        catch { }

        if (running && !_wasRunning)
        {
            IsGameRunning = true;
            _wasRunning = true;
            GameStarted?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("RocketLeague.exe detected as running");
        }
        else if (!running && _wasRunning)
        {
            IsGameRunning = false;
            _wasRunning = false;
            GameExited?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("RocketLeague.exe exited");
        }
        else
        {
            IsGameRunning = running;
        }
    }

    public void Dispose() => Stop();
}

public interface ICosmeticCatalogService
{
    CosmeticsCatalog Load();
    IReadOnlyList<CosmeticItem> Filter(string? search, string? category, string? rarity);
}

public sealed class CosmeticCatalogService : ICosmeticCatalogService
{
    private readonly ILogger<CosmeticCatalogService> _logger;
    private CosmeticsCatalog? _catalog;

    public CosmeticCatalogService(ILogger<CosmeticCatalogService> logger) => _logger = logger;

    public CosmeticsCatalog Load()
    {
        if (_catalog != null) return _catalog;

        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("cosmetics.json not found at " + path);

        var json = File.ReadAllText(path);
        var catalog = JsonSerializer.Deserialize<CosmeticsCatalog>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize cosmetics.json");

        if (catalog.SchemaVersion < 1)
            throw new InvalidOperationException("Unsupported cosmetics schema version");

        if (catalog.Items.Count == 0)
            throw new InvalidOperationException("cosmetics.json contains no items");

        _catalog = catalog;
        _logger.LogInformation("Loaded {Count} cosmetic items", catalog.Items.Count);
        return catalog;
    }

    public IReadOnlyList<CosmeticItem> Filter(string? search, string? category, string? rarity)
    {
        var catalog = Load();
        IEnumerable<CosmeticItem> q = catalog.Items;

        if (!string.IsNullOrWhiteSpace(category) && category != "All")
            q = q.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(rarity))
            q = q.Where(i => string.Equals(i.Rarity, rarity, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i =>
                i.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                i.Tags.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        return q.ToList();
    }
}

public interface ICosmeticApplier
{
    IntegrationStatus Status { get; }
    Task<IntegrationStatus> InitializeAsync(CancellationToken ct = default);
    Task<ApplyResult> ApplyAsync(CosmeticItem item, CancellationToken ct = default);
    Task<ApplyResult> ResetAsync(CosmeticItem? item, CancellationToken ct = default);
    Task ShutdownAsync();
}

public sealed class PreviewOnlyCosmeticApplier : ICosmeticApplier
{
    private readonly IEacDetectionService _eac;
    private readonly ILogger<PreviewOnlyCosmeticApplier> _logger;
    private readonly HashSet<string> _appliedIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public IntegrationStatus Status { get; private set; } = IntegrationStatus.NotInitialized;

    public PreviewOnlyCosmeticApplier(IEacDetectionService eac, ILogger<PreviewOnlyCosmeticApplier> logger)
    {
        _eac = eac;
        _logger = logger;
    }

    public Task<IntegrationStatus> InitializeAsync(CancellationToken ct = default)
    {
        var scan = _eac.Scan();
        if (scan.State != EacDetectionState.NotDetected)
        {
            Status = IntegrationStatus.Error;
            _logger.LogWarning("Cosmetic applier refused to initialize – EAC scan was {State}", scan.State);
            return Task.FromResult(Status);
        }

        _initialized = true;
        Status = IntegrationStatus.PreviewOnly;
        _logger.LogInformation("PreviewOnlyCosmeticApplier initialized (no game integration)");
        return Task.FromResult(Status);
    }

    public Task<ApplyResult> ApplyAsync(CosmeticItem item, CancellationToken ct = default)
    {
        if (!_initialized || Status == IntegrationStatus.Error)
            return Task.FromResult(ApplyResult.Failed("Applier is not initialized or is in error state."));

        _appliedIds.Add(item.Id);
        Status = IntegrationStatus.AppliedLocally;
        _logger.LogInformation("Local preview applied for {Id}", item.Id);
        return Task.FromResult(ApplyResult.PreviewOnly(item.Name));
    }

    public Task<ApplyResult> ResetAsync(CosmeticItem? item, CancellationToken ct = default)
    {
        if (item is null)
            _appliedIds.Clear();
        else
            _appliedIds.Remove(item.Id);

        Status = _appliedIds.Count == 0 ? IntegrationStatus.PreviewOnly : IntegrationStatus.AppliedLocally;
        return Task.FromResult(ApplyResult.Reset(item?.Name));
    }

    public Task ShutdownAsync()
    {
        _appliedIds.Clear();
        _initialized = false;
        Status = IntegrationStatus.NotInitialized;
        _logger.LogInformation("PreviewOnlyCosmeticApplier shut down – all local state cleared");
        return Task.CompletedTask;
    }
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class SettingsService : ISettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RLOfflineLauncher");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger) => _logger = logger;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null || settings.SchemaVersion < 1)
                return new AppSettings();
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings – using defaults");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }
}

public interface ILogBuffer
{
    IReadOnlyList<string> Entries { get; }
    void Add(string line);
    void Clear();
    event EventHandler? Changed;
}

public sealed class LogBuffer : ILogBuffer
{
    private const int Capacity = 2000;
    private readonly Queue<string> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public event EventHandler? Changed;

    public void Add(string line)
    {
        lock (_lock)
        {
            _entries.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RLOfflineLauncher.Services;
using RLOfflineLauncher.ViewModels;

namespace RLOfflineLauncher;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("Dispatcher", args.Exception);
            args.Handled = true;
            MessageBox.Show("An unexpected error occurred. Details were written to the log.\n\n" + args.Exception.Message,
                "RL Offline Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogFatal("AppDomain", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal("TaskScheduler", args.Exception);
            args.SetObserved();
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Warm up settings / log
        var logBuffer = Services.GetRequiredService<ILogBuffer>();
        logBuffer.Add("Application starting");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<IProcessProvider, ProcessProvider>();
        services.AddSingleton<IEacDetectionService, EacDetectionService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUrlOpener, UrlOpener>();
        services.AddSingleton<ILaunchPlanner, LaunchPlanner>();
        services.AddSingleton<IGameLauncher, GameLauncher>();
        services.AddSingleton<IGameProcessMonitor, GameProcessMonitor>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ICosmeticCatalogService, CosmeticCatalogService>();
        services.AddSingleton<ICosmeticApplier, PreviewOnlyCosmeticApplier>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILogBuffer, LogBuffer>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<CosmeticsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogsViewModel>();
    }

    private static void LogFatal(string source, Exception ex)
    {
        try
        {
            Services?.GetService<ILogBuffer>()?.Add($"[{source}] FATAL: {ex}");
        }
        catch { /* last resort */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services.GetService<IHotkeyService>()?.Dispose();
            Services.GetService<IGameProcessMonitor>()?.Stop();
            Services.GetService<ICosmeticApplier>()?.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch { /* ignore on shutdown */ }
        base.OnExit(e);
    }
}

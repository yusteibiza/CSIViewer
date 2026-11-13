using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using CSIViewer.Application;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Services;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace CSIViewer;

public partial class App : XamlApplication
{
    public static App CurrentApp => (App)Current;
    public static IServiceProvider Services { get; private set; } = default!;
    public static bool IsUnattended { get; private set; }

    /// <summary>
    /// True when the window is acting as the CSIViewer Host: it shows the
    /// local ID and password and hides the remote-control navigation.
    /// </summary>
    public static bool IsHostMode { get; private set; }

    private Window? _window;
    private SingleInstanceService? _singleInstance;
    private TrayIconService? _tray;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            CrashLog.Write("Unhandled UI exception", e.Exception);
            try
            {
                var logger = Services?.GetService<ILogger<App>>();
                logger?.LogCritical(
                    LogEvents.UnhandledException,
                    e.Exception,
                    "Unhandled UI exception");
            }
            catch
            {
                // Never let logging take down the process.
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            CrashLog.Write("Unhandled AppDomain exception", e.ExceptionObject as Exception);
            try
            {
                Services?.GetService<ILogger<App>>()?.LogCritical(
                    LogEvents.UnhandledException,
                    e.ExceptionObject as Exception,
                    "Unhandled AppDomain exception");
            }
            catch
            {
                // Never let logging take down the process.
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task exception", e.Exception);
            try
            {
                Services?.GetService<ILogger<App>>()?.LogCritical(
                    LogEvents.BackgroundTaskFailed,
                    e.Exception,
                    "Unobserved task exception");
            }
            catch
            {
                // Never let logging take down the process.
            }

            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            LaunchCore();
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnLaunched failed", ex);
            throw;
        }
    }

    private void LaunchCore()
    {
        var commandLine = Environment.GetCommandLineArgs();
        var dataDirectory = ReadOption(commandLine, "--data-dir") ??
                            ReadOption(commandLine, "--datadir");
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            AppPaths.SetDataDirectory(dataDirectory);
        }

        IsUnattended = commandLine.Any(argument =>
            argument.Equals("--unattended", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--desatendido", StringComparison.OrdinalIgnoreCase));

        IsHostMode = commandLine.Any(argument =>
            argument.Equals("--host-mode", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--modo-host", StringComparison.OrdinalIgnoreCase));

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Environment.Exit(0);
            return;
        }

        var services = new ServiceCollection();
        var loggingOptions = new LoggingOptions
        {
            MinimumLevel = LoggingBootstrap.ReadInitialLevel(AppPaths.SettingsPath)
        };
        services.AddSingleton(loggingOptions);
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddFilter((_, level) => level >= loggingOptions.MinimumLevel);
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            builder.AddProvider(new JsonLinesFileLoggerProvider(
                AppPaths.LogsDirectory,
                loggingOptions,
                "csiviewer"));
        });
        services.AddCSIViewerInfrastructure();
        services.AddCSIViewerClient();
        services.AddSingleton(loggingOptions);
        services.AddSingleton<IConsentService, ConsentService>();
        services.AddSingleton<INotificationService, NotificationService>();

        Services = services.BuildServiceProvider();

        Services.GetRequiredService<ILogger<App>>().LogInformation(
            LogEvents.AppStarted,
            "CSIViewer iniciado. Unattended={Unattended} HostMode={HostMode}",
            IsUnattended,
            IsHostMode);

        var state = Services.GetRequiredService<AppState>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        state.UiInvoker = action => dispatcher.TryEnqueue(() => action());

        _window = new MainWindow();
        if (!IsUnattended)
        {
            _window.Activate();
        }

        _tray = new TrayIconService(state, Services.GetRequiredService<RemoteClient>());
        _tray.Initialize();

        var notifications = (NotificationService)Services.GetRequiredService<INotificationService>();
        notifications.Register();

        _ = InitializeAsync(state);
    }

    public void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private static string? ReadOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(name.Length + 1)..].Trim('"');
            }

            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Length)
            {
                return arguments[index + 1].Trim('"');
            }
        }

        return null;
    }

    private async Task InitializeAsync(AppState state)    {
        try
        {
            var bootstrap = Services.GetRequiredService<ClientBootstrap>();
            await bootstrap.InitializeAsync();

            state.Post(() =>
            {
                ThemeService.Apply(state.Settings.Theme);
            });
        }
        catch (Exception ex)
        {
            var logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogError(ex, "Application initialization failed");
            state.Post(() =>
            {
                state.LastError = "No se pudo inicializar la aplicación. Revisa la configuración del servidor.";
                state.ServerStatus = "Error de inicialización";
            });
        }
    }
}

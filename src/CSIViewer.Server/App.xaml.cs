using Microsoft.UI.Xaml;

namespace CSIViewer.Server;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static App CurrentApp => (App)Current;
    public static ServerCommandLine CommandLine { get; private set; } = ServerCommandLine.Parse([]);

    private Window? _window;

    public App() : this(ServerCommandLine.Parse([]))
    {
    }

    public App(ServerCommandLine commandLine)
    {
        CommandLine = commandLine;
        InitializeComponent();
        UnhandledException += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            CrashLog.Write("Unhandled UI exception", eventArgs.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            CrashLog.Write("Unhandled AppDomain exception", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            CrashLog.Write("Unobserved task exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(CommandLine);
        _window.Activate();
    }
}

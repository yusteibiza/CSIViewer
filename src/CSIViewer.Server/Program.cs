using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace CSIViewer.Server;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var commandLine = ServerCommandLine.Parse(args);
        if (commandLine.ShowHelp)
        {
            EnsureConsole();
            ServerCommandLine.PrintHelp();
            return 0;
        }

        if (commandLine.Mode == ServerRunMode.Ui)
        {
            if (!ServerSingleInstance.TryAcquire())
            {
                return 0;
            }

            try
            {
                return RunUi(commandLine);
            }
            catch (Exception ex)
            {
                CrashLog.Write("No se pudo iniciar la interfaz del servidor", ex);
                Console.Error.WriteLine("SERVER_UI_ERROR " + ex.Message);
                return 1;
            }
            finally
            {
                ServerSingleInstance.Dispose();
            }
        }

        if (commandLine.Mode == ServerRunMode.Server)
        {
            EnsureConsole();
        }

        return RunServerAsync(commandLine, commandLine.Mode == ServerRunMode.Service)
            .GetAwaiter()
            .GetResult();
    }

    private static int RunUi(ServerCommandLine commandLine)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(commandLine);
        });

        return 0;
    }

    private static async Task<int> RunServerAsync(ServerCommandLine commandLine, bool asWindowsService)
    {
        await using var host = new ServerHost();
        try
        {
            if (asWindowsService)
            {
                ServerServiceLog.Write(
                    $"Iniciando servicio. url={commandLine.Settings.ListenUrl} db={commandLine.Settings.DatabasePath}");
            }

            await host.StartAsync(
                    commandLine.Settings,
                    commandLine.RemainingArguments,
                    asWindowsService)
                .ConfigureAwait(false);
            Console.WriteLine($"SERVER_READY url={host.ActiveUrl} db={commandLine.Settings.DatabasePath}");
            Console.WriteLine("ENROLLMENT_CODE_CONFIGURED "
                + (string.IsNullOrWhiteSpace(commandLine.Settings.EnrollmentCode) ? global::CSIViewer.Core.Localization.Text("no") : "yes"));

            foreach (var message in host.UpnpMessages)
            {
                Console.WriteLine("UPNP " + message);
            }

            if (asWindowsService)
            {
                ServerServiceLog.Write($"Servicio escuchando en {host.ActiveUrl}");
            }

            if (asWindowsService)
            {
                await host.WaitForShutdownAsync().ConfigureAwait(false);
            }
            else
            {
                var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    stop.TrySetResult();
                };
                AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
                await stop.Task.ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SERVER_ERROR " + ex.GetBaseException().Message);
            if (asWindowsService)
            {
                ServerServiceLog.Write("ERROR " + ex);
            }

            return 1;
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
            if (asWindowsService)
            {
                ServerServiceLog.Write("Servicio detenido.");
            }
        }
    }

    /// <summary>
    /// Attaches to the parent terminal when started from a console, or creates
    /// a console window for explicit server mode. UI mode never calls this, so
    /// no console window flashes before the WinUI window appears.
    /// </summary>
    private static void EnsureConsole()
    {
        try
        {
            if (HasRedirectedStandardOutput())
            {
                // A script or service manager owns the handles. Keep them so
                // SERVER_READY and errors reach the caller.
                return;
            }

            if (GetConsoleWindow() == IntPtr.Zero)
            {
                if (!AttachConsole(AttachParentProcess))
                {
                    AllocConsole();
                }
            }

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true
            });
            Console.SetError(new StreamWriter(Console.OpenStandardError())
            {
                AutoFlush = true
            });
            Console.Title = global::CSIViewer.Core.Localization.Text("CSIViewer Servidor");
        }
        catch
        {
            // The server still works if no console can be attached.
        }
    }

    private static bool HasRedirectedStandardOutput()
    {
        var handle = GetStdHandle(-11);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        var type = GetFileType(handle);
        return type is 1 or 3; // FILE_TYPE_DISK or FILE_TYPE_PIPE
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr hFile);
}

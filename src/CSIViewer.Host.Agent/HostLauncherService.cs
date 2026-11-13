using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace CSIViewer.Host.Agent;

public sealed class HostLauncherService : BackgroundService
{
    private readonly HostLauncherOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private DateTimeOffset _lastUiLookupUtc = DateTimeOffset.MinValue;
    private string? _resolvedUi;
    private bool _missingUiLogged;

    public HostLauncherService(HostLauncherOptions options, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ServiceLog.Write($"CSIViewerHost iniciado. UI={_options.UiExecutable}");
        if (_options.RunOnce)
        {
            TryLaunch();
            _lifetime.StopApplication();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TryLaunch();
            }
            catch (Exception ex)
            {
                ServiceLog.Write("Error en el bucle del servicio: " + ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        ServiceLog.Write("CSIViewerHost detenido.");
    }

    private void TryLaunch()
    {
        var ui = ResolveUi();
        if (string.IsNullOrWhiteSpace(ui))
        {
            if (!_missingUiLogged)
            {
                _missingUiLogged = true;
                ServiceLog.Write("No se encontró CSIViewer.Host.exe. Reinstala CSIViewer Host.");
            }

            return;
        }

        _missingUiLogged = false;
        var sessions = EnumerateActiveSessions();
        if (sessions.Count == 0)
        {
            ServiceLog.Write("No hay sesiones de usuario activas.");
            return;
        }

        foreach (var sessionId in sessions)
        {
            if (!IsHostUiRunning(sessionId))
            {
                TryLaunchSession(sessionId, ui);
            }
        }
    }

    private string? ResolveUi()
    {
        if (!string.IsNullOrWhiteSpace(_options.UiExecutable) &&
            File.Exists(_options.UiExecutable))
        {
            return _options.UiExecutable;
        }

        if (!string.IsNullOrWhiteSpace(_resolvedUi) &&
            File.Exists(_resolvedUi))
        {
            return _resolvedUi;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastUiLookupUtc < TimeSpan.FromSeconds(10))
        {
            return _resolvedUi;
        }

        _lastUiLookupUtc = now;
        _resolvedUi = HostUiLocator.Find();
        return _resolvedUi;
    }

    private void TryLaunchSession(uint sessionId, string ui)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            ServiceLog.Write(
                $"WTSQueryUserToken falló para la sesión {sessionId}: {Marshal.GetLastWin32Error()}. " +
                "El servicio debe ejecutarse como LocalSystem.");
            return;
        }

        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(
                    userToken,
                    0x02000000, // MAXIMUM_ALLOWED
                    IntPtr.Zero,
                    2, // SecurityImpersonation
                    1, // TokenPrimary
                    out primaryToken))
            {
                ServiceLog.Write("DuplicateTokenEx falló: " + Marshal.GetLastWin32Error());
                return;
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                environment = IntPtr.Zero;
            }

            var commandLine =
                $"\"{ui}\" {_options.UiArguments}";
            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default",
                dwFlags = 0x00000001, // STARTF_USESHOWWINDOW
                wShowWindow = 0 // SW_HIDE
            };

            var workingDirectory = Path.GetDirectoryName(ui);
            if (!CreateProcessAsUser(
                    primaryToken,
                    ui,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    0x00000400, // CREATE_UNICODE_ENVIRONMENT
                    environment,
                    workingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                ServiceLog.Write("CreateProcessAsUser falló: " + Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                ServiceLog.Write(
                    $"Host iniciado en la sesión {sessionId}. PID={processInfo.dwProcessId}");
            }
            finally
            {
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                DestroyEnvironmentBlock(environment);
            }

            if (primaryToken != IntPtr.Zero)
            {
                CloseHandle(primaryToken);
            }

            CloseHandle(userToken);
        }
    }

    private static List<uint> EnumerateActiveSessions()
    {
        var sessions = new List<uint>();
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out var count))
            {
                ServiceLog.Write("WTSEnumerateSessions falló: " + Marshal.GetLastWin32Error());
                return sessions;
            }

            var size = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(
                    IntPtr.Add(buffer, index * size));
                if (info.State == WTS_CONNECTSTATE_CLASS.WTSActive && info.SessionId > 0)
                {
                    sessions.Add((uint)info.SessionId);
                }
            }
        }
        catch (Exception ex)
        {
            ServiceLog.Write("Error enumerando sesiones: " + ex.Message);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                WTSFreeMemory(buffer);
            }
        }

        return sessions;
    }

    private static bool IsHostUiRunning(uint sessionId)
    {
        foreach (var process in Process.GetProcessesByName("CSIViewer.Host"))
        {
            try
            {
                if ((uint)process.SessionId == sessionId)
                {
                    return true;
                }
            }
            catch
            {
                // The process may have exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive = 0,
        WTSConnected = 1,
        WTSConnectQuery = 2,
        WTSShadow = 3,
        WTSDisconnected = 4,
        WTSIdle = 5,
        WTSListen = 6,
        WTSReset = 7,
        WTSDown = 8,
        WTSInit = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(
        IntPtr server,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        IntPtr token,
        bool inherit);

    [DllImport("userenv.dll")]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

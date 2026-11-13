using System.Runtime.InteropServices;

namespace CSIViewer.Server;

/// <summary>
/// Keeps a single server UI per user session. A second launch activates the
/// existing window instead of creating another one.
/// </summary>
public static class ServerSingleInstance
{
    private const string MutexName = @"Local\CSIViewerServidor.UI";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        // Same diagnostic switch as viewer/host, restricted here to an explicit
        // isolated data directory. Never needed for ordinary UI/service use.
        if (string.Equals(Environment.GetEnvironmentVariable("CSIVIEWER_ALLOW_MULTI"), "1", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSIVIEWER_SERVER_DATA_DIR")))
        {
            return true;
        }

        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            BringExistingWindowToFront();
            return false;
        }
        catch
        {
            // If Windows cannot create the mutex, allow the UI to start.
            return true;
        }
    }

    public static void Dispose()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // The process may not own the mutex anymore.
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    private static void BringExistingWindowToFront()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var window = FindWindow(null, global::CSIViewer.Core.Localization.Text("CSIViewer Servidor"));
                if (window != IntPtr.Zero)
                {
                    ActivateWindow(window);
                    return;
                }
            }
            catch
            {
                // Try again while the first instance finishes creating its window.
            }

            Thread.Sleep(150);
        }
    }

    private static void ActivateWindow(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(window, out _);
        var attached = false;
        if (foregroundThread != 0 &&
            targetThread != 0 &&
            foregroundThread != targetThread)
        {
            attached = AttachThreadInput(foregroundThread, targetThread, true);
        }

        try
        {
            ShowWindowAsync(window, SwRestore);
            BringWindowToTop(window);
            SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShow);
            SetWindowPos(window, HwndNotTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShow);
            SetForegroundWindow(window);
            SwitchToThisWindow(window, true);
            FlashWindow(window, true);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(foregroundThread, targetThread, false);
            }
        }
    }

    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hWnd, bool invert);
}

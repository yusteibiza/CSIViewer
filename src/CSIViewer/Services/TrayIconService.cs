using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CSIViewer.Services;
using WinRT.Interop;

namespace CSIViewer.Services;

public sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTrayCallback = WmApp + 1;
    private const int WmCommand = 0x0111;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int MfString = 0x00000000;
    private const int TpmRightButton = 0x0002;
    private const int TpmReturnCmd = 0x0100;
    private const int TpmNonotify = 0x0080;
    private const int SwRestore = 9;
    private const int IdiApplication = 32512;
    private const int ImageIcon = 1;
    private const int LrLoadFromFile = 0x00000010;
    private const int LrDefaultSize = 0x00000040;
    private const int CommandOpen = 1;
    private const int CommandEndSession = 2;
    private const int CommandExit = 3;

    private readonly AppState _state;
    private readonly RemoteClient _client;
    private readonly WndProc _wndProc;
    private readonly string _className = "CSIViewerTrayWindow." + Guid.NewGuid().ToString("N");
    private IntPtr _window;
    private IntPtr _icon;
    private uint _taskbarCreatedMessage;
    private bool _added;

    public TrayIconService(AppState state, RemoteClient client)
    {
        _state = state;
        _client = client;
        _wndProc = WindowProc;
    }

    public void Initialize()
    {
        try
        {
            var instance = GetModuleHandle(null);
            var windowClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = instance,
                lpszClassName = _className
            };
            RegisterClassEx(ref windowClass);

            _window = CreateWindowEx(
                0x00000080, // WS_EX_TOOLWINDOW
                _className,
                "CSIViewerTray",
                0,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (_window == IntPtr.Zero)
            {
                return;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Appicon.ico");
            _icon = File.Exists(iconPath)
                ? LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize)
                : IntPtr.Zero;
            if (_icon == IntPtr.Zero)
            {
                _icon = LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);
            }
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            _state.PropertyChanged += State_PropertyChanged;
            _state.SessionChanged += State_SessionChanged;
            AddIcon();
        }
        catch
        {
            // The tray icon is optional; the window remains fully usable.
        }
    }

    private void State_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.IsSessionActive) or nameof(AppState.DeviceId) or
            nameof(AppState.RemoteDeviceName) or nameof(AppState.ServerStatus))
        {
            MainWindow.Instance?.DispatcherQueue.TryEnqueue(UpdateIcon);
        }
    }

    private void State_SessionChanged(object? sender, EventArgs e)
    {
        MainWindow.Instance?.DispatcherQueue.TryEnqueue(UpdateIcon);
    }

    private void AddIcon()
    {
        if (_window == IntPtr.Zero || _added)
        {
            return;
        }

        var data = CreateIconData();
        _added = Shell_NotifyIcon(NimAdd, ref data);
    }

    private void UpdateIcon()
    {
        if (_window == IntPtr.Zero || !_added)
        {
            return;
        }

        var data = CreateIconData();
        Shell_NotifyIcon(NimModify, ref data);
    }

    private NOTIFYICONDATA CreateIconData()
    {
        var tooltip = _state.IsSessionActive
            ? $"CSIViewer — sesión con {_state.RemoteDeviceName}"
            : $"CSIViewer — ID {_state.DeviceId}";
        if (tooltip.Length > 120)
        {
            tooltip = tooltip[..120];
        }

        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayCallback,
            hIcon = _icon,
            szTip = tooltip
        };
    }

    private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _added = false;
            AddIcon();
            return IntPtr.Zero;
        }

        if (message == WmTrayCallback)
        {
            var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage == 0x0203) // WM_LBUTTONDBLCLK
            {
                OpenMainWindow();
            }
            else if (mouseMessage == 0x0205) // WM_RBUTTONUP
            {
                ShowMenu();
            }

            return IntPtr.Zero;
        }

        if (message == WmCommand)
        {
            switch (wParam.ToInt32())
            {
                case CommandOpen:
                    OpenMainWindow();
                    break;
                case CommandEndSession:
                    _ = _client.EndCurrentSessionsAsync();
                    break;
                case CommandExit:
                    Dispose();
                    MainWindow.Instance?.SaveWindowPlacementNow();
                    MainWindow.Instance?.AllowClose();
                    Microsoft.UI.Xaml.Application.Current.Exit();
                    break;
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, CommandOpen, "Abrir CSIViewer");
            AppendMenu(menu, MfString, CommandEndSession, "Finalizar sesión");
            AppendMenu(menu, 0x00000800, 0, null); // MF_SEPARATOR
            AppendMenu(menu, MfString, CommandExit, "Salir");

            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCmd | TpmNonotify,
                point.X,
                point.Y,
                0,
                _window,
                IntPtr.Zero);

            if (command == CommandOpen)
            {
                OpenMainWindow();
            }
            else if (command == CommandEndSession)
            {
                _ = _client.EndCurrentSessionsAsync();
            }
            else if (command == CommandExit)
            {
                Dispose();
                MainWindow.Instance?.SaveWindowPlacementNow();
                MainWindow.Instance?.AllowClose();
                Microsoft.UI.Xaml.Application.Current.Exit();
            }

            PostMessage(_window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static void OpenMainWindow()
    {
        var window = MainWindow.Instance;
        if (window is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        ShowWindow(hwnd, SwRestore);
        window.Activate();
        SetForegroundWindow(hwnd);
    }

    public void Dispose()
    {
        if (_added && _window != IntPtr.Zero)
        {
            var data = CreateIconData();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        _state.PropertyChanged -= State_PropertyChanged;
        _state.SessionChanged -= State_SessionChanged;

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        UnregisterClass(_className, GetModuleHandle(null));
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, int flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int newItemId, string? newItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu, int flags, int x, int y, int reserved, IntPtr window, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}

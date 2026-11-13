using System.Runtime.InteropServices;
using CSIViewer.Protocol;

namespace CSIViewer.Media;

public readonly record struct MonitorBounds(int X, int Y, int Width, int Height);
public sealed record InputInjectionDiagnostics(
    int AbsoluteX, int AbsoluteY, MonitorBounds VirtualDesktop,
    uint Flags, uint Requested, uint Inserted, int LastError,
    int? CursorX, int? CursorY, int DpiAwareness);

public sealed class InputInjector
{
    private readonly PressedInputState _pressed = new();

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x1000;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;

    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const uint KeyScanCode = 0x0008;

    public bool Send(InputEventPayload input, MonitorBounds? monitor = null)
        => Send(input, monitor, out _);

    public bool Send(InputEventPayload input, MonitorBounds? monitor,
        out InputInjectionDiagnostics? diagnostics)
    {
        diagnostics = null;
        try
        {
            var sent = input.Kind switch
            {
                "mouse" => SendMouse(input, monitor, out diagnostics),
                "key" => SendKey(input),
                "text" => SendText(input.Action),
                _ => false
            };

            if (sent)
            {
                _pressed.Observe(input);
            }

            return sent;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Releases every key and mouse button that is still logically pressed.
    /// Called when a control session ends or the remote link is lost so the
    /// host does not keep a key or button stuck.
    /// </summary>
    public int ReleaseAll()
    {
        var releases = _pressed.GetReleaseEvents();
        var released = 0;
        foreach (var release in releases)
        {
            try
            {
                var sent = release.Kind switch
                {
                    "mouse" => SendMouse(release, null),
                    "key" => SendKey(release),
                    _ => false
                };
                if (sent)
                {
                    released++;
                }
            }
            catch
            {
                // Best effort: never let input cleanup break session teardown.
            }
        }

        return released;
    }

    public void ClearPressedState() => _pressed.Clear();

    private static bool SendMouse(InputEventPayload input, MonitorBounds? monitor)
        => SendMouse(input, monitor, out _);

    private static bool SendMouse(InputEventPayload input, MonitorBounds? monitor,
        out InputInjectionDiagnostics? diagnostics)
    {
        diagnostics = null;
        var flags = input.Action switch
        {
            "move" => MouseMove,
            "leftDown" => MouseLeftDown,
            "leftUp" => MouseLeftUp,
            "rightDown" => MouseRightDown,
            "rightUp" => MouseRightUp,
            "middleDown" => MouseMiddleDown,
            "middleUp" => MouseMiddleUp,
            "wheel" => MouseWheel,
            "hwheel" => MouseHWheel,
            _ => 0u
        };

        if (flags == 0)
        {
            return false;
        }

        var hasPosition = input.HasPosition;
        if (hasPosition)
        {
            flags |= MouseMove | MouseAbsolute | MouseVirtualDesk;
        }

        var (dx, dy) = hasPosition
            ? MapToVirtualDesktop(input.X, input.Y, monitor)
            : (0, 0);

        var mouse = new MOUSEINPUT
        {
            dx = dx,
            dy = dy,
            mouseData = input.Action is "wheel" or "hwheel" ? (uint)input.Delta : 0,
            dwFlags = flags
        };

        var inputs = new[]
        {
            new INPUT
            {
                type = InputMouse,
                U = new INPUTUNION { mi = mouse }
            }
        };

        var inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        var error = Marshal.GetLastWin32Error();
        if (inserted == inputs.Length && input.Action == "move" && !input.TraceSampled)
        {
            return true;
        }
        // Read immediately after injection; this is an observation, not proof
        // that a target application processed the event (or of physical pixels).
        var hasCursor = GetCursorPos(out var cursor);
        diagnostics = new InputInjectionDiagnostics(dx, dy,
            new MonitorBounds(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN)),
            flags, (uint)inputs.Length, inserted, error,
            hasCursor ? cursor.X : null, hasCursor ? cursor.Y : null,
            GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()));
        return inserted == inputs.Length;
    }

    private static bool SendKey(InputEventPayload input)
    {
        var flags = KeyScanCode | (input.Down ? 0u : KeyUp);
        if (input.Extended)
        {
            flags |= KeyExtended;
        }

        var key = new KEYBDINPUT
        {
            wVk = 0,
            wScan = (ushort)input.ScanCode,
            dwFlags = flags
        };

        var inputs = new[]
        {
            new INPUT
            {
                type = InputKeyboard,
                U = new INPUTUNION { ki = key }
            }
        };

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static (int X, int Y) MapToVirtualDesktop(
        int normalizedX,
        int normalizedY,
        MonitorBounds? monitor)
    {
        var virtualX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var virtualY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var virtualWidth = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var virtualHeight = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var bounds = monitor is { Width: > 0, Height: > 0 }
            ? monitor.Value
            : new MonitorBounds(virtualX, virtualY, virtualWidth, virtualHeight);

        return CSIViewer.Core.MonitorCoordinateMapper.ToVirtualDesktopAbsolute(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            virtualX,
            virtualY,
            virtualWidth,
            virtualHeight,
            normalizedX,
            normalizedY);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    private static bool SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = new INPUT
            {
                type = InputKeyboard,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = text[i],
                        dwFlags = KeyUnicode
                    }
                }
            };
            inputs[i * 2 + 1] = new INPUT
            {
                type = InputKeyboard,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = text[i],
                        dwFlags = KeyUnicode | KeyUp
                    }
                }
            };
        }

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);
}

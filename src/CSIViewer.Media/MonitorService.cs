using System.Runtime.InteropServices;

namespace CSIViewer.Media;

public sealed record MonitorInfo(int Index, string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary, IntPtr Handle = default)
{
    public override string ToString() => $"Monitor {Index + 1} ({Width}x{Height}){(IsPrimary ? " · principal" : "")}";
}

public static class MonitorService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = ""
            };

            if (GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorInfo(
                    index++,
                    string.IsNullOrWhiteSpace(info.szDevice) ? $"DISPLAY{index}" : info.szDevice,
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    Math.Max(1, info.rcMonitor.Right - info.rcMonitor.Left),
                    Math.Max(1, info.rcMonitor.Bottom - info.rcMonitor.Top),
                    (info.dwFlags & 1) != 0,
                    hMonitor));
            }

            return true;
        }, IntPtr.Zero);

        return monitors.Count > 0
            ? monitors
            : [new MonitorInfo(0, "Pantalla principal", 0, 0, 1920, 1080, true)];
    }
}

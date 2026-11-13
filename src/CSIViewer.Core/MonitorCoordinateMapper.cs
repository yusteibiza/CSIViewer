namespace CSIViewer.Core;

/// <summary>
/// Converts normalized coordinates inside a selected monitor into the
/// 0..65535 virtual-desktop coordinates expected by MOUSEEVENTF_ABSOLUTE.
/// Handles monitors with negative origins (left/above the primary monitor).
/// </summary>
public static class MonitorCoordinateMapper
{
    public static (int X, int Y) ToVirtualDesktopAbsolute(
        int monitorX,
        int monitorY,
        int monitorWidth,
        int monitorHeight,
        int virtualX,
        int virtualY,
        int virtualWidth,
        int virtualHeight,
        int normalizedX,
        int normalizedY)
    {
        var width = Math.Max(1, monitorWidth);
        var height = Math.Max(1, monitorHeight);
        var vWidth = Math.Max(1, virtualWidth);
        var vHeight = Math.Max(1, virtualHeight);

        var nx = Math.Clamp(normalizedX, 0, 65535) / 65535.0;
        var ny = Math.Clamp(normalizedY, 0, 65535) / 65535.0;
        var screenX = monitorX + nx * width;
        var screenY = monitorY + ny * height;

        var dx = (int)Math.Round((screenX - virtualX) / vWidth * 65535);
        var dy = (int)Math.Round((screenY - virtualY) / vHeight * 65535);
        return (Math.Clamp(dx, 0, 65535), Math.Clamp(dy, 0, 65535));
    }
}

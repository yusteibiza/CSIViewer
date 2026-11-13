namespace CSIViewer.Core;

/// <summary>
/// Maps a point inside a surface that renders a remote image with Uniform
/// stretch into normalized 0..1 coordinates of the image itself. This avoids
/// the classic offset bug when host and client aspect ratios differ.
/// </summary>
public static class RemoteViewportMapper
{
    public static bool TryMapToNormalized(
        double surfaceWidth,
        double surfaceHeight,
        int sourceWidth,
        int sourceHeight,
        double x,
        double y,
        out double normalizedX,
        out double normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        if (surfaceWidth <= 0 || surfaceHeight <= 0 || x < 0 || y < 0)
        {
            return false;
        }

        var width = sourceWidth > 0 ? sourceWidth : surfaceWidth;
        var height = sourceHeight > 0 ? sourceHeight : surfaceHeight;
        var scale = Math.Min(surfaceWidth / width, surfaceHeight / height);
        if (scale <= 0)
        {
            return false;
        }

        var drawnWidth = width * scale;
        var drawnHeight = height * scale;
        var offsetX = (surfaceWidth - drawnWidth) / 2;
        var offsetY = (surfaceHeight - drawnHeight) / 2;
        var localX = x - offsetX;
        var localY = y - offsetY;
        if (localX < 0 || localY < 0 || localX > drawnWidth || localY > drawnHeight)
        {
            return false;
        }

        normalizedX = Math.Clamp(localX / drawnWidth, 0, 1);
        normalizedY = Math.Clamp(localY / drawnHeight, 0, 1);
        return true;
    }
}

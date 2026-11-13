namespace CSIViewer.Media;

/// <summary>
/// A captured frame. When H.264 is enabled the capturer produces raw BGRA and
/// avoids the JPEG intermediate. When JPEG is selected, <see cref="Jpeg"/> is
/// populated and <see cref="Bgra"/> may be empty.
/// </summary>
public sealed record CaptureFrame(
    byte[] Bgra,
    int Width,
    int Height,
    byte[]? Jpeg = null);

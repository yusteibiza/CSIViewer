namespace CSIViewer.Core;

public enum VideoPixelFormat
{
    Jpeg,
    Bgra
}

/// <summary>
/// A decoded video frame delivered to the UI. JPEG frames have unknown
/// dimensions until decoded; BGRA frames are ready for a WriteableBitmap.
/// </summary>
public sealed record VideoFrame(byte[] Data, int Width, int Height, VideoPixelFormat Format);

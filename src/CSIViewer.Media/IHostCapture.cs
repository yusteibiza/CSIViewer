namespace CSIViewer.Media;

public interface IHostCapture : IAsyncDisposable
{
    void Start(MonitorInfo monitor, int fps, int maxWidth, int jpegQuality, bool encodeJpeg,
        Action<CaptureFrame> onFrame, Action<Exception>? onError = null);
    Task StopAsync();
}

namespace CSIViewer.Media;

public sealed class SystemDesktopCapture : IHostCapture
{
    private readonly SystemDesktopThread _thread = new();
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private readonly SystemDesktopState _state;
    public SystemDesktopCapture(SystemDesktopState? state = null) => _state = state ?? new SystemDesktopState();

    public void Start(MonitorInfo monitor, int fps, int maxWidth, int jpegQuality, bool encodeJpeg,
        Action<CaptureFrame> onFrame, Action<Exception>? onError = null)
    {
        StopAsync().GetAwaiter().GetResult();
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frame = _thread.Invoke(_ =>
                    {
                        var monitors = MonitorService.GetMonitors();
                        var current = monitors.FirstOrDefault(x => x.Index == monitor.Index) ?? monitors[0];
                        var captured = GdiScreenCapture.CaptureOnce(current, maxWidth, jpegQuality, encodeJpeg);
                        _state.Captured(_thread.DesktopName);
                        return captured;
                    });
                    onFrame(frame);
                }
                catch (Exception ex) { onError?.Invoke(ex); }
                await Task.Delay(TimeSpan.FromSeconds(1.0 / Math.Clamp(fps, 1, 30)), token).ConfigureAwait(false);
            }
        }, token);
    }

    public async Task StopAsync()
    {
        if (_stop is null) return;
        _stop.Cancel();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        _stop.Dispose();
        _stop = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _thread.Dispose(); }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace CSIViewer.Media;

/// <summary>
/// Captura GDI compatible con el escritorio completo. Es la primera implementacion funcional;
/// el pipeline H.264/Windows.Graphics.Capture queda como optimizacion posterior.
/// </summary>
public sealed class GdiScreenCapture : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start(
        MonitorInfo monitor,
        int fps,
        int maxWidth,
        int jpegQuality,
        bool encodeJpeg,
        Action<CaptureFrame> onFrame,
        Action<Exception>? onError = null)
    {
        StopAsync().GetAwaiter().GetResult();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoopAsync(
            monitor, fps, maxWidth, jpegQuality, encodeJpeg, onFrame, onError, _cts.Token));
    }

    private static async Task CaptureLoopAsync(
        MonitorInfo monitor,
        int fps,
        int maxWidth,
        int jpegQuality,
        bool encodeJpeg,
        Action<CaptureFrame> onFrame,
        Action<Exception>? onError,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(fps, 1, 120));
        var scale = Math.Min(1.0, (double)maxWidth / monitor.Width);
        var targetWidth = Math.Max(1, (int)(monitor.Width * scale));
        var targetHeight = Math.Max(1, (int)(monitor.Height * scale));

        var screenDc = GetDC(IntPtr.Zero);
        var sourceDc = CreateCompatibleDC(screenDc);
        var sourceBitmap = CreateCompatibleBitmap(screenDc, monitor.Width, monitor.Height);
        var previousSource = SelectObject(sourceDc, sourceBitmap);
        var scaledDc = CreateCompatibleDC(screenDc);
        var scaledBitmap = CreateCompatibleBitmap(screenDc, targetWidth, targetHeight);
        var previousScaled = SelectObject(scaledDc, scaledBitmap);
        _ = SetStretchBltMode(scaledDc, 4);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    if (!BitBlt(sourceDc, 0, 0, monitor.Width, monitor.Height, screenDc,
                            monitor.X, monitor.Y, SRCCOPY | CAPTUREBLT) ||
                        !StretchBlt(scaledDc, 0, 0, targetWidth, targetHeight, sourceDc, 0, 0,
                            monitor.Width, monitor.Height, SRCCOPY))
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var pixels = ReadPixels(scaledDc, scaledBitmap, targetWidth, targetHeight);
                    if (pixels.Length == 0)
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (encodeJpeg)
                    {
                        var bytes = await EncodeJpegAsync(
                                pixels, targetWidth, targetHeight, jpegQuality)
                            .ConfigureAwait(false);
                        if (bytes.Length > 0)
                        {
                            onFrame(new CaptureFrame([], 0, 0, bytes));
                        }
                    }
                    else
                    {
                        onFrame(new CaptureFrame(pixels, targetWidth, targetHeight));
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }

                var elapsed = Stopwatch.GetElapsedTime(started);
                var remaining = interval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Yield at least a little even if capture is slower than
                    // the target FPS, instead of spinning the CPU.
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            SelectObject(sourceDc, previousSource);
            DeleteObject(sourceBitmap);
            DeleteDC(sourceDc);
            SelectObject(scaledDc, previousScaled);
            DeleteObject(scaledBitmap);
            DeleteDC(scaledDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[] ReadPixels(IntPtr dc, IntPtr bitmap, int width, int height)
    {
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        var pixels = new byte[width * height * 4];
        var result = GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0);
        return result == 0 ? [] : pixels;
    }

    private static async Task<byte[]> EncodeJpegAsync(byte[] pixels, int width, int height, int quality)
    {
        if (pixels.Length == 0)
        {
            return [];
        }

        var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
        using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Ignore);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var properties = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(
                Math.Clamp(quality / 100.0, 0.2, 0.95), PropertyType.Single)
        };
        await encoder.BitmapProperties.SetPropertiesAsync(properties);

        encoder.SetSoftwareBitmap(softwareBitmap);
        await encoder.FlushAsync();

        stream.Seek(0);
        if (stream.Size == 0)
        {
            return [];
        }

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines,
        byte[] bits, ref BITMAPINFO info, uint usage);
}

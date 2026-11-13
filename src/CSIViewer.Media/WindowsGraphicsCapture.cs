using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using WinRT;

namespace CSIViewer.Media;

/// <summary>
/// Captura con Windows.Graphics.Capture. Se usa cuando la interop D3D11 esta disponible;
/// si falla, RemoteClient mantiene el fallback GDI.
/// </summary>
public sealed class WindowsGraphicsCapture : IAsyncDisposable
{
    private readonly SemaphoreSlim _frameGate = new(1, 1);
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private GraphicsCaptureItem? _item;
    private Action<CaptureFrame>? _onFrame;
    private Action<Exception>? _onError;
    private TimeSpan _interval;
    private long _lastFrameTicks;
    private int _quality;
    private int _maxWidth;
    private bool _encodeJpeg;

    public bool TryStart(
        MonitorInfo monitor,
        int fps,
        int quality,
        int maxWidth,
        bool encodeJpeg,
        Action<CaptureFrame> onFrame,
        Action<Exception> onError)
    {
        StopAsync().GetAwaiter().GetResult();
        try
        {
            _onFrame = onFrame;
            _onError = onError;
            _quality = quality;
            _maxWidth = Math.Clamp(maxWidth, 640, 2560);
            _encodeJpeg = encodeJpeg;
            _interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(fps, 1, 120));
            _device = CreateDevice();
            _item = CreateItem(monitor);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);
            _session = _framePool.CreateCaptureSession(_item);
            try
            {
                // The controller already has a local pointer that maps 1:1 to
                // the host position. Capturing the host cursor as well would
                // draw a second, delayed pointer inside the streamed image.
                _session.IsCursorCaptureEnabled = false;
            }
            catch
            {
                // Older builds may not expose cursor capture; not fatal.
            }

            _framePool.FrameArrived += OnFrameArrived;
            _session.StartCapture();
            return true;
        }
        catch
        {
            StopAsync().GetAwaiter().GetResult();
            return false;
        }
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!await _frameGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            using var frame = sender.TryGetNextFrame();
            var now = Stopwatch.GetTimestamp();
            if (_lastFrameTicks != 0 && Stopwatch.GetElapsedTime(_lastFrameTicks, now) < _interval)
            {
                return;
            }

            _lastFrameTicks = now;
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            if (_encodeJpeg)
            {
                var bytes = await JpegEncoder.EncodeAsync(bitmap, _quality, _maxWidth)
                    .ConfigureAwait(false);
                if (bytes.Length > 0)
                {
                    _onFrame?.Invoke(new CaptureFrame([], 0, 0, bytes));
                }
            }
            else
            {
                var raw = JpegEncoder.GetBgra(bitmap, _maxWidth);
                if (raw.Bgra.Length > 0)
                {
                    _onFrame?.Invoke(new CaptureFrame(raw.Bgra, raw.Width, raw.Height));
                }
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _frameGate.Release();
        }
    }

    public Task StopAsync()
    {
        try
        {
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            _session?.Dispose();
            _framePool?.Dispose();
            (_device as IDisposable)?.Dispose();
            _item = null;
            _device = null;
        }
        catch
        {
            // Disposal is best effort.
        }
        finally
        {
            _onFrame = null;
            _onError = null;
            _session = null;
            _framePool = null;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static IDirect3DDevice CreateDevice()
    {
        var featureLevels = new[] { 0xB100, 0xB000, 0xA100, 0xA000 };
        var levelsHandle = GCHandle.Alloc(featureLevels, GCHandleType.Pinned);
        IntPtr devicePtr = IntPtr.Zero;
        IntPtr contextPtr = IntPtr.Zero;
        IntPtr dxgiPtr = IntPtr.Zero;
        IntPtr inspectablePtr = IntPtr.Zero;
        try
        {
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                1, // D3D_DRIVER_TYPE_HARDWARE
                IntPtr.Zero,
                0x20, // D3D11_CREATE_DEVICE_BGRA_SUPPORT
                levelsHandle.AddrOfPinnedObject(),
                (uint)featureLevels.Length,
                7,
                out devicePtr,
                out _,
                out contextPtr);
            Marshal.ThrowExceptionForHR(hr);

            var dxgiIid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            hr = Marshal.QueryInterface(devicePtr, in dxgiIid, out dxgiPtr);
            Marshal.ThrowExceptionForHR(hr);

            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiPtr, out inspectablePtr);
            Marshal.ThrowExceptionForHR(hr);
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectablePtr);
        }
        finally
        {
            levelsHandle.Free();
            if (dxgiPtr != IntPtr.Zero) Marshal.Release(dxgiPtr);
            if (contextPtr != IntPtr.Zero) Marshal.Release(contextPtr);
            if (devicePtr != IntPtr.Zero) Marshal.Release(devicePtr);
        }
    }

    private static GraphicsCaptureItem CreateItem(MonitorInfo monitor)
    {
        if (monitor.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("El monitor no tiene handle nativo.");
        }

        var classId = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var hr = WindowsCreateString(classId, classId.Length, out var hstring);
        Marshal.ThrowExceptionForHR(hr);
        IntPtr factoryPtr = IntPtr.Zero;
        IntPtr itemPtr = IntPtr.Zero;
        try
        {
            var interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            hr = RoGetActivationFactory(hstring, ref interopIid, out factoryPtr);
            Marshal.ThrowExceptionForHR(hr);

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            hr = interop.CreateForMonitor(monitor.Handle, ref itemIid, out itemPtr);
            Marshal.ThrowExceptionForHR(hr);
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            WindowsDeleteString(hstring);
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);
}

public static class JpegEncoder
{
    public static (byte[] Bgra, int Width, int Height) GetBgra(SoftwareBitmap bitmap, int maxWidth)
    {
        SoftwareBitmap? scaled = null;
        try
        {
            var effective = bitmap;
            if (maxWidth > 0 && bitmap.PixelWidth > maxWidth)
            {
                scaled = ScaleBgra(bitmap, maxWidth);
                effective = scaled;
            }

            var buffer = new Windows.Storage.Streams.Buffer(
                (uint)(effective.PixelWidth * effective.PixelHeight * 4));
            effective.CopyToBuffer(buffer);
            using var reader = DataReader.FromBuffer(buffer);
            var bytes = new byte[(int)buffer.Length];
            reader.ReadBytes(bytes);
            return (bytes, effective.PixelWidth, effective.PixelHeight);
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    public static async Task<byte[]> EncodeBgraAsync(
        byte[] bgra,
        int width,
        int height,
        int quality,
        int maxWidth)
    {
        if (bgra.Length < width * height * 4 || width <= 0 || height <= 0)
        {
            return [];
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(bgra),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Ignore);
        return await EncodeAsync(bitmap, quality, maxWidth).ConfigureAwait(false);
    }

    public static async Task<byte[]> EncodeAsync(SoftwareBitmap bitmap, int quality, int maxWidth)
    {
        SoftwareBitmap? scaled = null;
        try
        {
            if (bitmap.PixelWidth > maxWidth)
            {
                scaled = ScaleBgra(bitmap, maxWidth);
            }

            return await EncodeCoreAsync(scaled ?? bitmap, quality).ConfigureAwait(false);
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    private static SoftwareBitmap ScaleBgra(SoftwareBitmap source, int maxWidth)
    {
        var targetWidth = Math.Max(1, maxWidth);
        var targetHeight = Math.Max(1, (int)(source.PixelHeight * (double)targetWidth / source.PixelWidth));
        var sourceBuffer = new Windows.Storage.Streams.Buffer((uint)(source.PixelWidth * source.PixelHeight * 4));
        source.CopyToBuffer(sourceBuffer);
        using var reader = DataReader.FromBuffer(sourceBuffer);
        var sourceBytes = new byte[(int)sourceBuffer.Length];
        reader.ReadBytes(sourceBytes);

        var target = BgraScaler.Downscale(
            sourceBytes,
            source.PixelWidth,
            source.PixelHeight,
            targetWidth);

        return SoftwareBitmap.CreateCopyFromBuffer(
            CryptographicBuffer.CreateFromByteArray(target),
            BitmapPixelFormat.Bgra8,
            targetWidth,
            targetHeight,
            BitmapAlphaMode.Ignore);
    }

    private static async Task<byte[]> EncodeCoreAsync(SoftwareBitmap bitmap, int quality)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var properties = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(
                Math.Clamp(quality / 100.0, 0.2, 0.95), Windows.Foundation.PropertyType.Single)
        };
        await encoder.BitmapProperties.SetPropertiesAsync(properties);
        encoder.SetSoftwareBitmap(bitmap);
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
}

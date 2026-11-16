using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using WinRT;
using CSIViewer.Core;

namespace CSIViewer.Media;

/// <summary>
/// Captura con Windows.Graphics.Capture. Se usa cuando la interop D3D11 esta disponible;
/// si falla, RemoteClient mantiene el fallback GDI.
/// </summary>
public sealed class WindowsGraphicsCapture : IAsyncDisposable
{
    // A GPU copy from a retired run may never finish. It must not hold the gate
    // or callbacks of a replacement run (or publish stale frames into GDI).
    private sealed class CaptureRun(int fps, int quality, int maxWidth, bool encodeJpeg,
        Action<CaptureFrame> onFrame, Action<Exception> onError)
    {
        public readonly SemaphoreSlim FrameGate = new(1, 1);
        public readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(fps, 1, 120));
        public readonly int Quality = quality;
        public readonly int MaxWidth = Math.Clamp(maxWidth, 640, 2560);
        public readonly bool EncodeJpeg = encodeJpeg;
        public readonly Action<CaptureFrame> OnFrame = onFrame;
        public readonly Action<Exception> OnError = onError;
        public Direct3D11CaptureFramePool? Pool;
        public GraphicsCaptureSession? Session;
        public IDirect3DDevice? Device;
        public GraphicsCaptureItem? Item;
        public TypedEventHandler<Direct3D11CaptureFramePool, object>? Handler;
        public long LastFrameTicks;
        public int Stopped;
    }

    private CaptureRun? _run;
    private readonly object _lifecycleGate = new();
    private Task _cleanupTask = Task.CompletedTask;

    public bool TryStart(
        MonitorInfo monitor,
        int fps,
        int quality,
        int maxWidth,
        bool encodeJpeg,
        Action<CaptureFrame> onFrame,
        Action<Exception> onError)
    {
        // RemoteClient awaits normal shutdown with a deadline before TryStart.
        // If native GPU cleanup is stuck, decline WGC and let GDI take over.
        var run = new CaptureRun(fps, quality, maxWidth, encodeJpeg, onFrame, onError);
        lock (_lifecycleGate)
        {
            if (!StopAsync().IsCompletedSuccessfully) return false;
            _run = run;
        }
        try
        {
            run.Device = CreateDevice();
            run.Item = CreateItem(monitor);
            run.Pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                run.Device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                run.Item.Size);
            run.Session = run.Pool.CreateCaptureSession(run.Item);
            try
            {
                // The controller already has a local pointer that maps 1:1 to
                // the host position. Capturing the host cursor as well would
                // draw a second, delayed pointer inside the streamed image.
                run.Session.IsCursorCaptureEnabled = false;
            }
            catch
            {
                // Older builds may not expose cursor capture; not fatal.
            }

            run.Handler = (sender, _) => OnFrameArrived(run, sender);
            run.Pool.FrameArrived += run.Handler;
            run.Session.StartCapture();
            PipelineTrace.Record(PipelineStage.CaptureState, a: 1, b: monitor.Index);
            return true;
        }
        catch (Exception ex)
        {
            PipelineTrace.Record(PipelineStage.GraphicError, a: ex.HResult, b: 1);
            _ = StopAsync();
            return false;
        }
    }

    private async void OnFrameArrived(CaptureRun run, Direct3D11CaptureFramePool sender)
    {
        PipelineTrace.Record(PipelineStage.CaptureCallback);
        if (Volatile.Read(ref run.Stopped) != 0 || !await run.FrameGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null || Volatile.Read(ref run.Stopped) != 0) return;
            var now = Stopwatch.GetTimestamp();
            if (run.LastFrameTicks != 0 && Stopwatch.GetElapsedTime(run.LastFrameTicks, now) < run.Interval)
            {
                return;
            }

            run.LastFrameTicks = now;
            var diagnosticId = PipelineTrace.NextFrame();
            PipelineTrace.Record(PipelineStage.CaptureBegin, diagnosticId, a: frame.ContentSize.Width, b: frame.ContentSize.Height);
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().ConfigureAwait(false);
            PipelineTrace.Record(PipelineStage.CaptureDone, diagnosticId, a: bitmap.PixelWidth, b: bitmap.PixelHeight);
            if (Volatile.Read(ref run.Stopped) != 0) return;
            if (run.EncodeJpeg)
            {
                PipelineTrace.Record(PipelineStage.CaptureConversionBegin, diagnosticId);
                var bytes = await JpegEncoder.EncodeAsync(bitmap, run.Quality, run.MaxWidth)
                    .ConfigureAwait(false);
                PipelineTrace.Record(PipelineStage.CaptureConversionDone, diagnosticId, a: bytes.Length);
                if (bytes.Length > 0 && Volatile.Read(ref run.Stopped) == 0)
                {
                    run.OnFrame(new CaptureFrame([], 0, 0, bytes) { DiagnosticId = diagnosticId });
                }
            }
            else
            {
                PipelineTrace.Record(PipelineStage.CaptureConversionBegin, diagnosticId);
                var raw = JpegEncoder.GetBgra(bitmap, run.MaxWidth);
                PipelineTrace.Record(PipelineStage.CaptureConversionDone, diagnosticId, a: raw.Bgra.Length);
                if (raw.Bgra.Length > 0 && Volatile.Read(ref run.Stopped) == 0)
                {
                    run.OnFrame(new CaptureFrame(raw.Bgra, raw.Width, raw.Height) { DiagnosticId = diagnosticId });
                }
            }
        }
        catch (Exception ex)
        {
            PipelineTrace.Record(PipelineStage.GraphicError, a: ex.HResult, b: 2);
            if (Volatile.Read(ref run.Stopped) == 0) run.OnError(ex);
        }
        finally
        {
            run.FrameGate.Release();
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            var run = Interlocked.Exchange(ref _run, null);
            if (run is null) return _cleanupTask;
            Volatile.Write(ref run.Stopped, 1);
            PipelineTrace.Record(PipelineStage.CaptureState, a: 0);
            // Dispose can block inside a virtual graphics driver. Move it off the
            // caller so RemoteClient's shutdown deadline actually has an effect.
            _cleanupTask = Task.Run(() =>
            {
                try { if (run.Pool is not null && run.Handler is not null) run.Pool.FrameArrived -= run.Handler; } catch { }
                try { run.Session?.Dispose(); } catch { }
                try { run.Pool?.Dispose(); } catch { }
                try { (run.Device as IDisposable)?.Dispose(); } catch { }
            });
            return _cleanupTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { /* Do not hang app shutdown on a virtual GPU. */ }
    }

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
            if (inspectablePtr != IntPtr.Zero) Marshal.Release(inspectablePtr);
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
            if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
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
        var properties = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(
                Math.Clamp(quality / 100f, 0.2f, 0.95f), Windows.Foundation.PropertyType.Single)
        };
        // ImageQuality is a codec creation option, not image metadata.
        // Setting it via BitmapProperties can fail with 0x88982F41 on Win10.
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties);
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

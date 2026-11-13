using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace CSIViewer.Media;

internal static class H264Runtime
{
    private static readonly object Gate = new();
    private static bool _started;

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            var hr = PInvoke.MFStartup(0x00020070, 0);
            if (hr.Value < 0)
            {
                throw new InvalidOperationException($"No se pudo iniciar Media Foundation (0x{hr.Value:X8}).");
            }

            _started = true;
        }
    }
}

internal static class H264Guids
{
    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid MtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MtBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MtInterlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MtProfile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid MtIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid MtPixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid EncoderClsid = new("6CA50344-051A-4DED-9779-A43305165E35");
    public static readonly Guid DecoderClsid = new("62CE7E72-4C71-4D20-B15D-452831A87D9D");
    public static readonly Guid MfLowLatency = new("9C27891A-ED7A-40E1-88E8-B22727A024EE");
    public static readonly Guid MfDefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    public static readonly Guid CodecApiDefaultBPictureCount = new("8D390AAC-DC5C-4200-B57F-814D04BABAB2");
}

internal static unsafe class H264MediaTypeHelpers
{
    public static void SetUInt64(IMFMediaType type, Guid key, ulong value)
    {
        var localKey = key;
        type.SetUINT64(&localKey, value);
    }

    public static void SetUInt32(IMFMediaType type, Guid key, uint value)
    {
        var localKey = key;
        type.SetUINT32(&localKey, value);
    }
}

[ComImport]
[Guid("901DB4C7-31CE-41A2-85DC-8FA0BF41B8DA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICodecApiRaw
{
    [PreserveSig] int IsSupported(ref Guid api);
    [PreserveSig] int IsModifiable(ref Guid api);
    [PreserveSig] int GetParameterRange(ref Guid api, IntPtr valueMin, IntPtr valueMax, IntPtr steppingDelta);
    [PreserveSig] int GetParameterValues(ref Guid api, out IntPtr values, out uint valuesCount);
    [PreserveSig] int GetDefaultValue(ref Guid api, IntPtr value);
    [PreserveSig] int GetValue(ref Guid api, IntPtr value);
    [PreserveSig] int SetValue(ref Guid api, IntPtr value);
}

internal static class CodecApiVariant
{
    private const ushort VtUi4 = 19;
    private const int VariantSize = 24;

    [StructLayout(LayoutKind.Explicit, Size = VariantSize)]
    private struct VariantUi4
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public uint Value;
    }

    public static int SetUInt32(ICodecApiRaw codec, Guid property, uint value)
    {
        var variant = new VariantUi4 { Vt = VtUi4, Value = value };
        var pointer = Marshal.AllocHGlobal(VariantSize);
        try
        {
            Marshal.StructureToPtr(variant, pointer, false);
            return codec.SetValue(ref property, pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}

public sealed class H264Encoder : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly IMFTransform _transform;
    private long _sampleIndex;
    private bool _disposed;

    public H264Encoder(int width, int height, int fps, int bitrate)
    {
        H264Runtime.EnsureStarted();
        _width = Math.Max(16, width & ~1);
        _height = Math.Max(16, height & ~1);
        _fps = Math.Clamp(fps, 1, 120);
        _transform = CreateTransform(H264Guids.EncoderClsid);
        ConfigureLowLatency(_transform);

        unsafe
        {
            PInvoke.MFCreateMediaType(out var inputType);
            SetGuid(inputType, H264Guids.MtMajorType, H264Guids.MediaTypeVideo);
            SetGuid(inputType, H264Guids.MtSubtype, H264Guids.VideoFormatNv12);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtFrameSize, ((ulong)(uint)_width << 32) | (uint)_height);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtFrameRate, ((ulong)_fps << 32) | 1UL);
            H264MediaTypeHelpers.SetUInt32(inputType, H264Guids.MtInterlace, 2);
            H264MediaTypeHelpers.SetUInt32(inputType, H264Guids.MtIndependent, 1);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtPixelAspect, ((ulong)1 << 32) | 1UL);

            PInvoke.MFCreateMediaType(out var outputType);
            SetGuid(outputType, H264Guids.MtMajorType, H264Guids.MediaTypeVideo);
            SetGuid(outputType, H264Guids.MtSubtype, H264Guids.VideoFormatH264);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtFrameSize, ((ulong)(uint)_width << 32) | (uint)_height);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtFrameRate, ((ulong)_fps << 32) | 1UL);
            H264MediaTypeHelpers.SetUInt32(outputType, H264Guids.MtBitrate, (uint)Math.Clamp(bitrate, 500_000, 25_000_000));
            H264MediaTypeHelpers.SetUInt32(outputType, H264Guids.MtInterlace, 2);
            H264MediaTypeHelpers.SetUInt32(outputType, H264Guids.MtProfile, 100);
            H264MediaTypeHelpers.SetUInt32(outputType, H264Guids.MtIndependent, 1);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtPixelAspect, ((ulong)1 << 32) | 1UL);

            _transform.SetOutputType(0, outputType, 0);
            _transform.SetInputType(0, inputType, 0);
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, UIntPtr.Zero);
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_START_OF_STREAM, UIntPtr.Zero);
        }
    }

    public byte[] Encode(byte[] bgra)
    {
        if (_disposed)
        {
            return [];
        }

        if (bgra.Length < _width * _height * 4)
        {
            return [];
        }

        var nv12Length = _width * _height * 3 / 2;
        unsafe
        {
            PInvoke.MFCreateMemoryBuffer((uint)nv12Length, out var buffer);
            buffer.Lock(out var ptr, out _, out _);
            fixed (byte* source = bgra)
            {
                BgraToNv12(source, _width, _height, (byte*)ptr);
            }

            buffer.Unlock();
            buffer.SetCurrentLength((uint)nv12Length);

            PInvoke.MFCreateSample(out var sample);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(_sampleIndex * (10_000_000 / _fps));
            sample.SetSampleDuration(10_000_000 / _fps);
            _sampleIndex++;

            _transform.ProcessInput(0, sample, 0);
            var outputs = new List<byte[]>(2);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                PInvoke.MFCreateMemoryBuffer((uint)Math.Max(_width * _height * 2, 1_048_576), out var outBuffer);
                PInvoke.MFCreateSample(out var outSample);
                outSample.AddBuffer(outBuffer);
                var buffers = new MFT_OUTPUT_DATA_BUFFER[1];
                buffers[0].dwStreamID = 0;
                buffers[0].pSample = outSample;
                try
                {
                    _transform.ProcessOutput(0, 1, buffers, out _);
                    var produced = buffers[0].pSample ?? outSample;
                    produced.ConvertToContiguousBuffer(out var contiguous);
                    contiguous.Lock(out var outPtr, out _, out var length);
                    var data = new byte[length];
                    Marshal.Copy((IntPtr)outPtr, data, 0, data.Length);
                    contiguous.Unlock();
                    outputs.Add(data);
                }
                catch (COMException ex)
                {
                    if (ex.HResult == unchecked((int)0xC00D6D72))
                    {
                        break;
                    }

                    throw;
                }
            }

            if (outputs.Count == 0)
            {
                return [];
            }

            if (outputs.Count == 1)
            {
                return outputs[0];
            }

            var total = outputs.Sum(x => x.Length);
            var combined = new byte[total];
            var offset = 0;
            foreach (var output in outputs)
            {
                output.AsSpan().CopyTo(combined.AsSpan(offset));
                offset += output.Length;
            }

            return combined;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            unsafe
            {
                _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_END_OF_STREAM, UIntPtr.Zero);
                _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_COMMAND_DRAIN, UIntPtr.Zero);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static unsafe void ConfigureLowLatency(IMFTransform transform)
    {
        try
        {
            // Without MF_LOW_LATENCY the Microsoft H.264 MFT buffers about
            // 17 frames before producing the first output (hundreds of ms at
            // 12-30 FPS). With this attribute the first frame is emitted
            // immediately and every captured frame is encoded in order.
            if (transform is IMFAttributes attributes)
            {
                var attribute = H264Guids.MfLowLatency;
                attributes.SetUINT32(&attribute, 1);
            }

            if (transform is ICodecApiRaw codecApi)
            {
                _ = CodecApiVariant.SetUInt32(
                    codecApi,
                    H264Guids.CodecApiDefaultBPictureCount,
                    0);
            }
        }
        catch
        {
            // Low-latency hints are optional; the encoder still works.
        }
    }

    private static IMFTransform CreateTransform(Guid clsid)
    {
        var hr = PInvoke.CoCreateInstance(in clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out IMFTransform transform);
        if (hr.Value < 0)
        {
            throw new InvalidOperationException($"No se pudo crear el MFT (0x{hr.Value:X8}).");
        }

        return transform;
    }

    private static unsafe void SetGuid(IMFMediaType type, Guid key, Guid value)
        => type.SetGUID(&key, &value);

    internal static unsafe void BgraToNv12(byte* bgra, int width, int height, byte* nv12)
    {
        var yPlaneSize = width * height;
        var uvWidth = width / 2;
        var uvHeight = height / 2;

        Parallel.For(0, height, y =>
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var index = (row + x) * 4;
                var b = bgra[index];
                var g = bgra[index + 1];
                var r = bgra[index + 2];
                var yy = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                nv12[row + x] = (byte)Math.Clamp(yy, 16, 235);
            }
        });

        Parallel.For(0, uvHeight, y =>
        {
            var sourceRow = y * 2 * width;
            var targetRow = yPlaneSize + y * uvWidth * 2;
            for (var x = 0; x < uvWidth; x++)
            {
                var sumB = 0;
                var sumG = 0;
                var sumR = 0;
                for (var dy = 0; dy < 2; dy++)
                {
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var index = ((sourceRow + dy * width) + x * 2 + dx) * 4;
                        sumB += bgra[index];
                        sumG += bgra[index + 1];
                        sumR += bgra[index + 2];
                    }
                }

                var bAvg = sumB / 4;
                var gAvg = sumG / 4;
                var rAvg = sumR / 4;
                var u = ((-38 * rAvg - 74 * gAvg + 112 * bAvg + 128) >> 8) + 128;
                var v = ((112 * rAvg - 94 * gAvg - 18 * bAvg + 128) >> 8) + 128;
                nv12[targetRow + x * 2] = (byte)Math.Clamp(u, 16, 240);
                nv12[targetRow + x * 2 + 1] = (byte)Math.Clamp(v, 16, 240);
            }
        });
    }
}

public sealed class H264Decoder : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly IMFTransform _transform;
    private readonly int _quality;
    private long _sampleIndex;
    private bool _disposed;

    public unsafe H264Decoder(int width, int height, int fps, int quality)
    {
        H264Runtime.EnsureStarted();
        _width = Math.Max(16, width & ~1);
        _height = Math.Max(16, height & ~1);
        _fps = Math.Clamp(fps, 1, 120);
        _quality = Math.Clamp(quality, 20, 95);
        _transform = CreateTransform();

        try
        {
            // IMFTransform exposes its attributes through GetAttributes, not
            // necessarily by implementing IMFAttributes on the transform.
            _transform.GetAttributes(out var attributes);
            var attribute = H264Guids.MfLowLatency;
            attributes.SetUINT32(&attribute, 1);
            if (_transform is ICodecApiRaw codecApi)
            {
                // Microsoft's H.264 decoder requires VT_UI4 here (unlike the
                // encoder's VT_BOOL). Documented in CODECAPI_AVLowLatencyMode.
                _ = CodecApiVariant.SetUInt32(codecApi, H264Guids.MfLowLatency, 1);
            }
        }
        catch
        {
            // Optional low-latency hint.
        }

        unsafe
        {
            PInvoke.MFCreateMediaType(out var inputType);
            SetGuid(inputType, H264Guids.MtMajorType, H264Guids.MediaTypeVideo);
            SetGuid(inputType, H264Guids.MtSubtype, H264Guids.VideoFormatH264);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtFrameSize, ((ulong)(uint)_width << 32) | (uint)_height);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtFrameRate, ((ulong)_fps << 32) | 1UL);
            H264MediaTypeHelpers.SetUInt32(inputType, H264Guids.MtInterlace, 2);
            H264MediaTypeHelpers.SetUInt64(inputType, H264Guids.MtPixelAspect, ((ulong)1 << 32) | 1UL);

            PInvoke.MFCreateMediaType(out var outputType);
            SetGuid(outputType, H264Guids.MtMajorType, H264Guids.MediaTypeVideo);
            SetGuid(outputType, H264Guids.MtSubtype, H264Guids.VideoFormatNv12);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtFrameSize, ((ulong)(uint)_width << 32) | (uint)_height);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtFrameRate, ((ulong)_fps << 32) | 1UL);
            H264MediaTypeHelpers.SetUInt32(outputType, H264Guids.MtInterlace, 2);
            H264MediaTypeHelpers.SetUInt64(outputType, H264Guids.MtPixelAspect, ((ulong)1 << 32) | 1UL);

            _transform.SetInputType(0, inputType, 0);
            try
            {
                _transform.GetOutputAvailableType(0, 0, out var availableType);
                H264MediaTypeHelpers.SetUInt64(
                    availableType,
                    H264Guids.MtFrameRate,
                    ((ulong)_fps << 32) | 1UL);
                _transform.SetOutputType(0, availableType, 0);
            }
            catch
            {
                _transform.SetOutputType(0, outputType, 0);
            }
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, UIntPtr.Zero);
            _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_START_OF_STREAM, UIntPtr.Zero);
        }
    }

    public Task<IReadOnlyList<CSIViewer.Core.VideoFrame>> DecodeAsync(byte[] annexB)
    {
        if (_disposed || annexB.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<CSIViewer.Core.VideoFrame>>([]);
        }

        var nv12Frames = DecodeNv12Frames(annexB);
        var frames = new List<CSIViewer.Core.VideoFrame>(nv12Frames.Count);
        foreach (var nv12 in nv12Frames)
        {
            var bgra = Nv12ToBgra(nv12.Data, _width, _height, nv12.Stride, nv12.PlaneHeight);
            frames.Add(new CSIViewer.Core.VideoFrame(
                bgra,
                _width,
                _height,
                CSIViewer.Core.VideoPixelFormat.Bgra));
        }

        return Task.FromResult<IReadOnlyList<CSIViewer.Core.VideoFrame>>(frames);
    }

    private unsafe bool TryRenegotiateOutputType()
    {
        try
        {
            _transform.GetOutputAvailableType(0, 0, out var type);
            H264MediaTypeHelpers.SetUInt64(
                type,
                H264Guids.MtFrameRate,
                ((ulong)_fps << 32) | 1UL);
            _transform.SetOutputType(0, type, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private unsafe (int Stride, int PlaneHeight) GetOutputLayout()
    {
        var stride = _width;
        var planeHeight = _height;
        try
        {
            _transform.GetOutputCurrentType(0, out var type);
            var key = H264Guids.MfDefaultStride;
            try
            {
                type.GetUINT32(&key, out var value);
                if (value > 0 && value <= int.MaxValue) stride = (int)value;
            }
            catch (COMException) { /* Compact stride if not advertised. */ }

            key = H264Guids.MtFrameSize;
            type.GetUINT64(&key, out var size);
            var codedHeight = (uint)size;
            if (codedHeight >= _height && codedHeight <= 16384)
            {
                // NV12 chroma starts after the coded luma plane, not after
                // the visible crop (e.g. 1088 rows for a 1080-line desktop).
                planeHeight = (int)codedHeight;
            }
        }
        catch
        {
            // Retain the last available compact layout if metadata is absent.
        }
        return (stride, planeHeight);
    }

    private unsafe List<(byte[] Data, int Stride, int PlaneHeight)> DecodeNv12Frames(byte[] annexB)
    {
        var frames = new List<(byte[] Data, int Stride, int PlaneHeight)>();
        PInvoke.MFCreateMemoryBuffer((uint)annexB.Length, out var buffer);
        buffer.Lock(out var ptr, out _, out _);
        Marshal.Copy(annexB, 0, (IntPtr)ptr, annexB.Length);
        buffer.Unlock();
        buffer.SetCurrentLength((uint)annexB.Length);
        PInvoke.MFCreateSample(out var sample);
        sample.AddBuffer(buffer);
        sample.SetSampleTime(_sampleIndex * (10_000_000 / _fps));
        sample.SetSampleDuration(10_000_000 / _fps);
        _sampleIndex++;

        void DrainOutputs(int max)
        {
            for (var attempt = 0; attempt < max; attempt++)
            {
                PInvoke.MFCreateMemoryBuffer(
                    (uint)Math.Max(_width * _height * 2, 1_048_576), out var outBuffer);
                PInvoke.MFCreateSample(out var outSample);
                outSample.AddBuffer(outBuffer);
                var buffers = new MFT_OUTPUT_DATA_BUFFER[1];
                buffers[0].dwStreamID = 0;
                buffers[0].pSample = outSample;
                try
                {
                    _transform.ProcessOutput(0, 1, buffers, out _);
                    var produced = buffers[0].pSample ?? outSample;
                    produced.ConvertToContiguousBuffer(out var contiguous);
                    contiguous.Lock(out var outPtr, out _, out var length);
                    var nv12 = new byte[length];
                    Marshal.Copy((IntPtr)outPtr, nv12, 0, nv12.Length);
                    contiguous.Unlock();
                    var layout = GetOutputLayout();
                    frames.Add((nv12, layout.Stride, layout.PlaneHeight));
                }
                catch (COMException ex)
                {
                    if (ex.HResult == unchecked((int)0xC00D6D72))
                    {
                        break;
                    }

                    if (ex.HResult == unchecked((int)0xC00D6D61) &&
                        TryRenegotiateOutputType())
                    {
                        // The decoder signalled a new sequence (SPS/PPS or
                        // resolution); the output type is now set and the same
                        // input can be retried.
                        continue;
                    }

                    break;
                }
            }
        }

        try
        {
            _transform.ProcessInput(0, sample, 0);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0xC00D36B5))
        {
            // The decoder may still hold delayed frames from a previous input.
            // Drain them before retrying instead of losing the whole stream.
            DrainOutputs(16);
            _transform.ProcessInput(0, sample, 0);
        }

        // A decoder can emit several delayed frames in one call. Drain until
        // MF_E_TRANSFORM_NEED_MORE_INPUT instead of assuming one frame.
        DrainOutputs(16);
        return frames;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            unsafe
            {
                _transform.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_NOTIFY_END_OF_STREAM, UIntPtr.Zero);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static IMFTransform CreateTransform()
    {
        var clsid = H264Guids.DecoderClsid;
        var hr = PInvoke.CoCreateInstance(in clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out IMFTransform transform);
        if (hr.Value < 0)
        {
            throw new InvalidOperationException($"No se pudo crear el decodificador (0x{hr.Value:X8}).");
        }

        return transform;
    }

    private static unsafe void SetGuid(IMFMediaType type, Guid key, Guid value)
        => type.SetGUID(&key, &value);

    internal static byte[] Nv12ToBgra(byte[] nv12, int width, int height, int stride, int planeHeight = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException("NV12 dimensions must be even.");
        }
        var rowStride = Math.Max(width, stride);
        var codedHeight = planeHeight == 0 ? height : planeHeight;
        ArgumentOutOfRangeException.ThrowIfLessThan(codedHeight, height, nameof(planeHeight));
        var uvPlane = checked(rowStride * codedHeight);
        var required = checked(uvPlane + (height / 2 - 1) * rowStride + width);
        if (nv12.Length < required)
        {
            throw new ArgumentException("Truncated NV12 buffer.", nameof(nv12));
        }
        var bgra = new byte[checked(width * height * 4)];
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var yValue = nv12[y * rowStride + x] - 16;
                var uvIndex = uvPlane + (y / 2) * rowStride + (x / 2) * 2;
                var uValue = nv12[uvIndex] - 128;
                var vValue = nv12[uvIndex + 1] - 128;

                var c = 1.164 * yValue;
                var r = Clamp(c + 1.596 * vValue);
                var g = Clamp(c - 0.392 * uValue - 0.813 * vValue);
                var b = Clamp(c + 2.017 * uValue);
                var index = (y * width + x) * 4;
                bgra[index] = b;
                bgra[index + 1] = g;
                bgra[index + 2] = r;
                bgra[index + 3] = 255;
            }
        });

        return bgra;

        static byte Clamp(double value) => (byte)Math.Clamp((int)value, 0, 255);
    }
}

public static class JpegDecoder
{
    public static async Task<(byte[] Bgra, int Width, int Height)> DecodeToBgraAsync(byte[] jpeg)
    {
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(jpeg);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var buffer = new Windows.Storage.Streams.Buffer((uint)(width * height * 4));
        bitmap.CopyToBuffer(buffer);
        using var reader = DataReader.FromBuffer(buffer);
        var bgra = new byte[(int)buffer.Length];
        reader.ReadBytes(bgra);
        return (bgra, width, height);
    }
}

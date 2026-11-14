using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CSIViewer.Media;

/// <summary>BT.601 limited-range NV12, including padded luma planes, to opaque BGRA.</summary>
internal static class Nv12PixelConverter
{
    private static readonly ParallelOptions Workers = new() { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };

    internal static byte[] Convert(byte[] nv12, int width, int height, int stride, int planeHeight = 0, bool useSimd = true)
    {
        ArgumentNullException.ThrowIfNull(nv12);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((width & 1) != 0 || (height & 1) != 0) throw new ArgumentException("NV12 dimensions must be even.");
        var rowStride = Math.Max(width, stride);
        var codedHeight = planeHeight == 0 ? height : planeHeight;
        ArgumentOutOfRangeException.ThrowIfLessThan(codedHeight, height, nameof(planeHeight));
        var uvPlane = checked(rowStride * codedHeight);
        var required = checked(uvPlane + (height / 2 - 1) * rowStride + width);
        if (nv12.Length < required) throw new ArgumentException("Truncated NV12 buffer.", nameof(nv12));
        var bgra = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        var simd = useSimd && Avx2.IsSupported && Ssse3.IsSupported;
        var bands = (height + 31) / 32;
        if (width * (long)height < 640 * 360 || Workers.MaxDegreeOfParallelism == 1)
            Band(0, height);
        else Parallel.For(0, bands, Workers, band => Band(band * 32, Math.Min(height, (band + 1) * 32)));
        return bgra;

        void Band(int start, int end)
        {
            for (var y = start; y < end; y += 2)
            {
                var y0 = y * rowStride;
                var y1 = y0 + rowStride;
                var uv = uvPlane + y / 2 * rowStride;
                var output0 = y * width * 4;
                var output1 = output0 + width * 4;
                var x = 0;
                if (simd)
                {
                    var uMask = Vector128.Create((byte)0, 0, 2, 2, 4, 4, 6, 6, 128, 128, 128, 128, 128, 128, 128, 128);
                    var vMask = Vector128.Create((byte)1, 1, 3, 3, 5, 5, 7, 7, 128, 128, 128, 128, 128, 128, 128, 128);
                    for (; x <= width - 8; x += 8)
                    {
                        var chroma = Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref nv12[uv + x])).AsByte();
                        var u = Avx2.ConvertToVector256Int32(Ssse3.Shuffle(chroma, uMask)) - Vector256.Create(128);
                        var v = Avx2.ConvertToVector256Int32(Ssse3.Shuffle(chroma, vMask)) - Vector256.Create(128);
                        var r = v * Vector256.Create(26149);
                        var g = u * Vector256.Create(-6423) + v * Vector256.Create(-13320);
                        var b = u * Vector256.Create(33047);
                        WriteVector(y0 + x, output0 + x * 4, r, g, b);
                        WriteVector(y1 + x, output1 + x * 4, r, g, b);
                    }
                }
                for (; x < width; x += 2)
                {
                    var u = nv12[uv + x] - 128;
                    var v = nv12[uv + x + 1] - 128;
                    var r = 26149 * v;
                    var g = -6423 * u - 13320 * v;
                    var b = 33047 * u;
                    WritePixel(y0 + x, output0 + x * 4, r, g, b);
                    WritePixel(y0 + x + 1, output0 + (x + 1) * 4, r, g, b);
                    WritePixel(y1 + x, output1 + x * 4, r, g, b);
                    WritePixel(y1 + x + 1, output1 + (x + 1) * 4, r, g, b);
                }
            }
        }

        void WritePixel(int source, int target, int r, int g, int b)
        {
            var c = (nv12[source] - 16) * 19071;
            bgra[target] = (byte)Math.Clamp((c + b) >> 14, 0, 255);
            bgra[target + 1] = (byte)Math.Clamp((c + g) >> 14, 0, 255);
            bgra[target + 2] = (byte)Math.Clamp((c + r) >> 14, 0, 255);
            bgra[target + 3] = 255;
        }

        void WriteVector(int source, int target, Vector256<int> r, Vector256<int> g, Vector256<int> b)
        {
            var luma = Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref nv12[source])).AsByte();
            var c = (Avx2.ConvertToVector256Int32(luma) - Vector256.Create(16)) * Vector256.Create(19071);
            r = Vector256.Clamp((c + r) >> 14, Vector256<int>.Zero, Vector256.Create(255));
            g = Vector256.Clamp((c + g) >> 14, Vector256<int>.Zero, Vector256.Create(255));
            b = Vector256.Clamp((c + b) >> 14, Vector256<int>.Zero, Vector256.Create(255));
            var packed = b | (g << 8) | (r << 16) | Vector256.Create(unchecked((int)0xff000000));
            packed.AsByte().StoreUnsafe(ref MemoryMarshal.GetArrayDataReference(bgra), (nuint)target);
        }
    }
}

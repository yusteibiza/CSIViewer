using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CSIViewer.Media;

internal static class VideoBenchmark
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Run(string output)
    {
        var convert = typeof(H264Decoder).GetMethod("Nv12ToBgra", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<byte[], int, int, int, int, byte[]>>();
        var results = new List<object>();
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440) })
        {
            var nv12 = new byte[width * height * 3 / 2];
            new Random(42).NextBytes(nv12);
            Measure("NV12 legacy reference", () => LegacyConvert(nv12, width, height));
            Measure("NV12 to BGRA", () => convert(nv12, width, height, width, height));
            var source = convert(nv12, width, height, width, height);
            using var encoder = new H264Encoder(width, height, 60, 20_000_000);
            using var decoder = new H264Decoder(width, height, 60, 90);
            var packets = new List<byte[]>();
            for (var i = 0; i < 24; i++)
            {
                var packet = encoder.Encode(source);
                if (packet.Length > 0) packets.Add(packet);
            }
            // Replay a complete sequence in order, never arbitrary inter-frame packets.
            var decodeTimes = new List<double>();
            var transformTimes = new List<double>();
            var conversionTimes = new List<double>();
            var frames = 0;
            foreach (var packet in packets)
            {
                var start = Stopwatch.GetTimestamp();
                var decoded = decoder.DecodeAsync(packet).GetAwaiter().GetResult();
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (decoded.Count > 0)
                {
                    frames += decoded.Count; decodeTimes.Add(elapsed / decoded.Count);
                    transformTimes.Add(decoder.LastTransformMilliseconds / decoded.Count);
                    conversionTimes.Add(decoder.LastConversionMilliseconds / decoded.Count);
                }
            }
            Add("H264 decode including conversion", decodeTimes, frames);
            Add("H264 transform and copies", transformTimes, frames);
            Add("Decoded frame color conversion", conversionTimes, frames);

            void Measure(string name, Func<byte[]> action)
            {
                for (var i = 0; i < 12; i++) GC.KeepAlive(action());
                var times = new List<double>();
                for (var i = 0; i < 60; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    GC.KeepAlive(action());
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                Add(name, times, times.Count);
            }
            void Add(string name, List<double> times, int frames)
            {
                times.Sort();
                if (times.Count == 0) throw new InvalidOperationException("Benchmark produced no frames.");
                var median = times[times.Count / 2];
                var p95 = times[(int)Math.Floor((times.Count - 1) * .95)];
                results.Add(new { Name = name, Width = width, Height = height, Frames = frames, MedianMs = median, P95Ms = p95 });
                Console.WriteLine($"{name} {width}x{height}: median={median:F2} ms p95={p95:F2} ms frames={frames}");
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { ProcessorCount = Environment.ProcessorCount, Results = results }, JsonOptions));
    }

    // Original conversion retained only in this diagnostic for same-process A/B measurements.
    private static byte[] LegacyConvert(byte[] nv12, int width, int height)
    {
        var result = new byte[width * height * 4];
        var uvPlane = width * height;
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var c = 1.164 * (nv12[y * width + x] - 16);
                var uv = uvPlane + y / 2 * width + x / 2 * 2;
                var u = nv12[uv] - 128;
                var v = nv12[uv + 1] - 128;
                var i = (y * width + x) * 4;
                result[i] = (byte)Math.Clamp((int)(c + 2.017 * u), 0, 255);
                result[i + 1] = (byte)Math.Clamp((int)(c - .392 * u - .813 * v), 0, 255);
                result[i + 2] = (byte)Math.Clamp((int)(c + 1.596 * v), 0, 255);
                result[i + 3] = 255;
            }
        });
        return result;
    }
}

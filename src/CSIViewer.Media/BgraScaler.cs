namespace CSIViewer.Media;

/// <summary>
/// High-quality BGRA downscaler. The previous implementation sampled a single
/// source pixel per destination pixel (nearest neighbour), which aliased text
/// and small icons when the capture was reduced to MaxWidth. This versions
/// averages every source pixel that contributes to a destination pixel (box
/// filter), preserving edges and legibility much better for desktop content.
/// </summary>
public static class BgraScaler
{
    public static byte[] Downscale(byte[] source, int sourceWidth, int sourceHeight, int targetWidth)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || source.Length < sourceWidth * sourceHeight * 4)
        {
            return [];
        }

        var width = Math.Clamp(targetWidth, 1, sourceWidth);
        var height = Math.Max(1, (int)(sourceHeight * (double)width / sourceWidth));
        if (width == sourceWidth && height == sourceHeight)
        {
            return source;
        }

        var target = new byte[width * height * 4];
        var parallel = height >= 64 && width * height >= 65_536;

        void ScaleRows(int startY, int endY)
        {
            for (var y = startY; y < endY; y++)
            {
                var sourceY0 = (int)((long)y * sourceHeight / height);
                var sourceY1 = Math.Max(sourceY0 + 1, (int)(((long)y + 1) * sourceHeight / height));
                var targetRow = y * width;

                for (var x = 0; x < width; x++)
                {
                    var sourceX0 = (int)((long)x * sourceWidth / width);
                    var sourceX1 = Math.Max(sourceX0 + 1, (int)(((long)x + 1) * sourceWidth / width));

                    var sumB = 0;
                    var sumG = 0;
                    var sumR = 0;
                    var sumA = 0;
                    var count = 0;

                    for (var sy = sourceY0; sy < sourceY1; sy++)
                    {
                        var row = sy * sourceWidth;
                        for (var sx = sourceX0; sx < sourceX1; sx++)
                        {
                            var index = (row + sx) * 4;
                            sumB += source[index];
                            sumG += source[index + 1];
                            sumR += source[index + 2];
                            sumA += source[index + 3];
                            count++;
                        }
                    }

                    var targetIndex = (targetRow + x) * 4;
                    target[targetIndex] = (byte)(sumB / count);
                    target[targetIndex + 1] = (byte)(sumG / count);
                    target[targetIndex + 2] = (byte)(sumR / count);
                    target[targetIndex + 3] = (byte)(sumA / count);
                }
            }
        }

        if (parallel)
        {
            Parallel.For(
                0,
                Environment.ProcessorCount,
                worker =>
                {
                    var start = worker * height / Environment.ProcessorCount;
                    var end = (worker + 1) * height / Environment.ProcessorCount;
                    if (start < end)
                    {
                        ScaleRows(start, end);
                    }
                });
        }
        else
        {
            ScaleRows(0, height);
        }

        return target;
    }
}

namespace CSIViewer.Core;

/// <summary>
/// Single-producer, bounded RGB sampling. Only change/no-change is exported;
/// pixels and the internal fingerprint must never be written to diagnostics.
/// Unchanged samples do not prove that the whole image is unchanged.
/// </summary>
public sealed class FrameContentProbe
{
    private ulong _fingerprint;
    private int _width, _height;
    private bool _initialized;

    public bool TrySample(ReadOnlySpan<byte> bgra, int width, int height, out bool changed, out int samples)
    {
        changed = false;
        samples = 0;
        if (width <= 0 || height <= 0 || (long)width * height > bgra.Length / 4) return false;
        var columns = Math.Min(64, width);
        var rows = Math.Min(36, height);
        var fingerprint = 14695981039346656037UL;
        for (var row = 0; row < rows; row++)
        {
            var y = (int)((2L * row + 1) * height / (2 * rows));
            for (var column = 0; column < columns; column++)
            {
                var x = (int)((2L * column + 1) * width / (2 * columns));
                var offset = (y * width + x) * 4;
                unchecked
                {
                    fingerprint = (fingerprint ^ bgra[offset]) * 1099511628211UL;
                    fingerprint = (fingerprint ^ bgra[offset + 1]) * 1099511628211UL;
                    fingerprint = (fingerprint ^ bgra[offset + 2]) * 1099511628211UL;
                }
            }
        }
        samples = columns * rows;
        changed = !_initialized || width != _width || height != _height || fingerprint != _fingerprint;
        _fingerprint = fingerprint;
        _width = width;
        _height = height;
        _initialized = true;
        return true;
    }
}

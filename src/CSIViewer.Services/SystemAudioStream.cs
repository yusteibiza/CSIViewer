using System.Threading.Channels;
using CSIViewer.Protocol;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CSIViewer.Services;

/// <summary>A session-owned WASAPI loopback capture or playback, with bounded latency.</summary>
internal sealed class SystemAudioStream : IDisposable
{
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _player;
    private BufferedWaveProvider? _buffer;
    private readonly Channel<byte[]> _packets = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(4)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public void StartCapture(Func<byte[], CancellationToken, Task> send, Action<Exception> failed)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _capture = new WasapiLoopbackCapture();
            var format = _capture.WaveFormat;
            if (format is WaveFormatExtensible extensible) format = extensible.ToStandardWaveFormat();
            var floating = format.Encoding == WaveFormatEncoding.IeeeFloat;
            if ((!floating && format.Encoding != WaveFormatEncoding.Pcm) ||
                !AudioPacket.IsValidFormat(format.SampleRate, format.Channels, format.BitsPerSample, floating))
                throw new NotSupportedException("Unsupported output audio format");
            _capture.DataAvailable += (_, e) =>
            {
                if (_cts.IsCancellationRequested) return;
                var max = AudioPacket.MaxDataLength / format.BlockAlign * format.BlockAlign;
                for (var offset = 0; offset < e.BytesRecorded; offset += max)
                {
                    var length = Math.Min(max, e.BytesRecorded - offset);
                    length -= length % format.BlockAlign;
                    if (length > 0) _packets.Writer.TryWrite(AudioPacket.Create(format.SampleRate,
                        format.Channels, format.BitsPerSample, floating, e.Buffer.AsSpan(offset, length)));
                }
            };
            _capture.RecordingStopped += (_, e) => { if (e.Exception is { } error && !_cts.IsCancellationRequested) failed(error); };
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var packet in _packets.Reader.ReadAllAsync(_cts.Token))
                        await send(packet, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
                catch (Exception ex) { failed(ex); }
            });
            _capture.StartRecording();
        }
    }

    public void Play(byte[] packet)
    {
        if (!AudioPacket.TryRead(packet, out var rate, out var channels, out var bits, out var floating)) return;
        lock (_gate)
        {
            if (_disposed) return;
            var format = floating ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, bits, channels);
            // The format is fixed for one capture activation. Do not allow an
            // untrusted peer to repeatedly rebuild the native output device.
            if (_buffer is not null && !_buffer.WaveFormat.Equals(format)) return;
            if (_buffer is null)
            {
                _player?.Dispose();
                _buffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromMilliseconds(300), DiscardOnBufferOverflow = true };
                _player = new WasapiOut(AudioClientShareMode.Shared, 80);
                _player.Init(_buffer);
                _player.Play();
            }
            var length = packet.Length - AudioPacket.HeaderLength;
            if (_buffer.BufferedBytes + length > _buffer.BufferLength) _buffer.ClearBuffer();
            _buffer.AddSamples(packet, AudioPacket.HeaderLength, length);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _packets.Writer.TryComplete();
            try { _capture?.StopRecording(); } catch { /* Device may have disappeared. */ }
            _capture?.Dispose();
            _player?.Dispose();
            _buffer = null;
        }
    }
}

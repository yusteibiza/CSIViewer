using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using CSIViewer.Protocol;

namespace CSIViewer.Networking;

public sealed class SignalingClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private int _closedRaised;
    private int _disposed;

    public event EventHandler<Envelope>? EnvelopeReceived;
    public event EventHandler<byte[]>? BinaryReceived;
    public event EventHandler<string>? ConnectionClosed;
    public event EventHandler<Exception>? Error;

    public bool IsConnected => _socket.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_receiveTask is not null)
        {
            throw new InvalidOperationException("La conexión de señalización ya se inició.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cts.Token);
        await _socket.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    public Task SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        var json = ProtocolJson.Serialize(envelope);
        return SendRawAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, cancellationToken);
    }

    public Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken = default)
        => SendRawAsync(payload, WebSocketMessageType.Binary, cancellationToken);

    private async Task SendRawAsync(
        byte[] payload,
        WebSocketMessageType type,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("La conexión de señalización no está abierta.");
            }

            await _socket.SendAsync(payload, type, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var (type, data) = await WebSocketMessageReader
                    .ReadMessageAsync(_socket, buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (type is null)
                {
                    RaiseClosed("Conexión cerrada");
                    return;
                }

                if (type == WebSocketMessageType.Text)
                {
                    var envelope = ProtocolJson.Deserialize(Encoding.UTF8.GetString(data));
                    if (envelope is not null)
                    {
                        EnvelopeReceived?.Invoke(this, envelope);
                    }
                }
                else
                {
                    BinaryReceived?.Invoke(this, data);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
            RaiseClosed(ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void RaiseClosed(string reason)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _closedRaised, 1) == 0)
        {
            ConnectionClosed?.Invoke(this, reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Cierre",
                        timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Closing is best effort.
        }

        try
        {
            _socket.Abort();
        }
        catch
        {
            // The socket is already gone.
        }

        _socket.Dispose();

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // The receive loop is already stopping.
            }
        }

        var sendLockAcquired = false;
        try
        {
            sendLockAcquired = await _sendLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // The semaphore is already stopping.
        }

        if (sendLockAcquired)
        {
            _sendLock.Release();
            _sendLock.Dispose();
        }

        _cts.Dispose();
    }
}

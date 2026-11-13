using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSIViewer.Protocol;

namespace CSIViewer.Networking;

public sealed record DirectOffer(string EndPoints, string Token);

public sealed class DirectSessionServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly string _sessionId;
    private readonly CancellationTokenSource _cts = new();

    private DirectSessionServer(TcpListener listener, string sessionId, string token, int port)
    {
        _listener = listener;
        _sessionId = sessionId;
        Token = token;
        EndPoints = BuildEndpoints(port);
    }

    public string EndPoints { get; }
    public string Token { get; }

    public static DirectSessionServer Start(string sessionId)
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new DirectSessionServer(listener, sessionId, token, port);
    }

    public async Task<NetworkStream?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        while (!linked.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }

            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var preamble = await FrameProtocol.ReadFrameAsync(stream, linked.Token).ConfigureAwait(false);
                if (preamble is null)
                {
                    client.Dispose();
                    continue;
                }

                var hello = JsonSerializer.Deserialize<DirectHello>(preamble);
                if (hello is null ||
                    !string.Equals(hello.SessionId, _sessionId, StringComparison.Ordinal) ||
                    !string.Equals(hello.Token, Token, StringComparison.Ordinal))
                {
                    client.Dispose();
                    continue;
                }

                _listener.Stop();
                return stream;
            }
            catch
            {
                client.Dispose();
            }
        }

        return null;
    }

    private static string BuildEndpoints(int port)
    {
        var endpoints = new List<string>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                    {
                        endpoints.Add($"{address.Address}:{port}");
                    }
                }
            }
        }
        catch
        {
            // The loopback endpoint below is enough for same-machine testing.
        }

        endpoints.Add($"{IPAddress.Loopback}:{port}");
        return string.Join(';', endpoints.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
            _listener.Stop();
        }
        catch
        {
            // Best effort.
        }

        _cts.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record DirectHello(string SessionId, string Token);
}

public static class DirectSessionClient
{
    public static async Task<NetworkStream?> TryConnectAsync(
        string? endPoints,
        string? token,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endPoints) || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        foreach (var candidate in endPoints.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = candidate.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            {
                continue;
            }

            var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
                await client.ConnectAsync(parts[0], port, timeout.Token).ConfigureAwait(false);
                client.NoDelay = true;
                var stream = client.GetStream();
                var hello = new DirectHello(sessionId, token);
                await FrameProtocol.WriteFrameAsync(
                    stream,
                    JsonSerializer.SerializeToUtf8Bytes(hello),
                    timeout.Token).ConfigureAwait(false);
                return stream;
            }
            catch
            {
                client.Dispose();
            }
        }

        return null;
    }

    private sealed record DirectHello(string SessionId, string Token);
}

public static class FrameProtocol
{
    private const int MaxFrameSize = MessageValidation.MaxRelayMessageBytes;

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length <= 0 || payload.Length > MaxFrameSize)
        {
            throw new InvalidOperationException("Tamaño de frame de red no válido.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size <= 0 || size > MaxFrameSize)
        {
            return null;
        }

        var payload = new byte[size];
        return await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false) ? payload : null;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count <= 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }
}

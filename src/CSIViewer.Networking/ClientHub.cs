using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using CSIViewer.Protocol;

namespace CSIViewer.Networking;

public sealed class ClientConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly WebSocket _socket;

    public ClientConnection(WebSocket socket) => _socket = socket;

    /// <summary>Unique identifier for this WebSocket, even across reconnects.</summary>
    public Guid ConnectionId { get; } = Guid.NewGuid();

    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public bool Replaced { get; set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(DeviceId);

    public async Task SendJsonAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope));
        await SendRawAsync(data, WebSocketMessageType.Text, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendRawAsync(byte[] data, WebSocketMessageType type, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(data, type, true, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown disposed the connection while the send was unwinding.
            }
        }
    }

    /// <summary>
    /// Sends a droppable binary frame without waiting for the socket. If a send
    /// is already in progress (for example, a viewer that stopped reading),
    /// the frame is discarded instead of blocking every other session.
    /// </summary>
    public bool TrySendRaw(byte[] data, WebSocketMessageType type)
    {
        if (!_sendLock.Wait(0))
        {
            return false;
        }

        _ = SendHoldingLockAsync(data, type);
        return true;
    }

    private async Task SendHoldingLockAsync(byte[] data, WebSocketMessageType type)
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(data, type, true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // The receive loop will notice the broken connection.
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown disposed the connection while the send was unwinding.
            }
        }
    }

    public async Task CloseAsync(string reason)
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sendLock.Dispose();
    }
}

public sealed class SessionRoute
{
    public required Guid Id { get; init; }
    public required string ControllerId { get; init; }
    public required string HostId { get; init; }
    public Guid ControllerConnectionId { get; init; }
    public Guid HostConnectionId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool Accepted { get; set; }

    /// <summary>
    /// Returns the peer only when the caller is one of the two legitimate
    /// participants. A third authenticated device must never inject frames
    /// into or terminate another session.
    /// </summary>
    public bool TryGetPeer(string deviceId, out string peerDeviceId)
    {
        if (string.Equals(deviceId, ControllerId, StringComparison.Ordinal))
        {
            peerDeviceId = HostId;
            return true;
        }

        if (string.Equals(deviceId, HostId, StringComparison.Ordinal))
        {
            peerDeviceId = ControllerId;
            return true;
        }

        peerDeviceId = "";
        return false;
    }
}

public enum SessionAdmissionResult
{
    Accepted,
    DuplicateSession,
    ControllerBusy,
    HostFull
}

public sealed class ClientHub
{
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, SessionRoute> _sessions = new();
    private readonly object _admissionLock = new();

    public bool TryGet(string deviceId, out ClientConnection connection)
        => _clients.TryGetValue(deviceId, out connection!);

    public IReadOnlyCollection<ClientConnection> All => _clients.Values.ToArray();

    public ClientConnection? AddOrReplace(ClientConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.DeviceId))
        {
            return null;
        }

        ClientConnection? replaced = null;
        _clients.AddOrUpdate(
            connection.DeviceId,
            connection,
            (_, existing) =>
            {
                existing.Replaced = true;
                replaced = existing;
                return connection;
            });
        return replaced;
    }

    /// <summary>
    /// Immediately disconnects a device (for example after revocation). The
    /// connection's own cleanup removes its routes and notifies its peers.
    /// </summary>
    public async Task<bool> DisconnectDeviceAsync(
        string deviceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!_clients.TryRemove(deviceId, out var connection))
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await connection.CloseAsync(reason).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // The receive loop will still observe the closed socket.
        }

        return true;
    }

    public void Remove(ClientConnection connection)
    {
        if (connection.DeviceId is { } id &&
            _clients.TryGetValue(id, out var current) &&
            ReferenceEquals(current, connection))
        {
            _clients.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Atomically validates and registers a session route. The check and the
    /// insertion must be one operation, otherwise two simultaneous requests
    /// from the same controller or an eleventh request could both pass.
    /// </summary>
    public SessionAdmissionResult TryAdmitSession(SessionRoute route, int maxSessionsPerHost)
    {
        lock (_admissionLock)
        {
            if (_sessions.ContainsKey(route.Id))
            {
                return SessionAdmissionResult.DuplicateSession;
            }

            var hostSessions = _sessions.Values
                .Where(x => x.HostId == route.HostId)
                .ToArray();

            if (hostSessions.Any(x => x.ControllerId == route.ControllerId))
            {
                return SessionAdmissionResult.ControllerBusy;
            }

            if (hostSessions.Length >= Math.Max(1, maxSessionsPerHost))
            {
                return SessionAdmissionResult.HostFull;
            }

            return _sessions.TryAdd(route.Id, route)
                ? SessionAdmissionResult.Accepted
                : SessionAdmissionResult.DuplicateSession;
        }
    }

    public bool TryGetSession(Guid id, out SessionRoute route) => _sessions.TryGetValue(id, out route!);

    public void RemoveSession(Guid id)
    {
        lock (_admissionLock)
        {
            _sessions.TryRemove(id, out _);
        }
    }

    public IReadOnlyList<SessionRoute> SessionsFor(string deviceId)
        => _sessions.Values.Where(x => x.ControllerId == deviceId || x.HostId == deviceId).ToArray();

    /// <summary>
    /// Routes that use a specific WebSocket. Connection identity (not device
    /// identity) lets a reconnected device keep its new routes while the old
    /// socket cleans up only the routes it actually owned.
    /// </summary>
    public IReadOnlyList<SessionRoute> SessionsForConnection(Guid connectionId)
        => _sessions.Values
            .Where(x => x.ControllerConnectionId == connectionId || x.HostConnectionId == connectionId)
            .ToArray();

    public async Task BroadcastPresenceAsync(ClientConnection origin, bool online, CancellationToken cancellationToken)
    {
        var message = ProtocolJson.Create(MessageTypes.Presence,
            new PresenceMessage(origin.DeviceId!, online, origin.DeviceName ?? origin.DeviceId!,
                online ? null : DateTimeOffset.UtcNow));

        foreach (var client in _clients.Values)
        {
            if (client.DeviceId is not null && client.DeviceId != origin.DeviceId)
            {
                try
                {
                    using var timeout = new CancellationTokenSource();
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await client.SendJsonAsync(message, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // A stale connection will be removed by its own receive loop.
                }
            }
        }
    }

    public async Task SendPresenceSnapshotAsync(ClientConnection target, CancellationToken cancellationToken)
    {
        foreach (var client in _clients.Values)
        {
            if (client.DeviceId is not null && client.DeviceId != target.DeviceId)
            {
                try
                {
                    using var timeout = new CancellationTokenSource();
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await target.SendJsonAsync(
                        ProtocolJson.Create(MessageTypes.Presence,
                            new PresenceMessage(client.DeviceId, true, client.DeviceName ?? client.DeviceId)),
                        timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // The snapshot is best effort; the receive loop will clean up.
                }
            }
        }
    }
}

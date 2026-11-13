using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Sockets;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Media;
using CSIViewer.Networking;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private readonly ConcurrentDictionary<Guid, RemoteSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, IncomingPending> _incomingRequests = new();
    private readonly SemaphoreSlim _incomingAuthGate = new(1, 1);
    private readonly SessionManager _sessionManager = new();
    private volatile bool _revokingAccess;
    private int _captureViewerCount;

    private sealed class RemoteSession : IDisposable
    {
        public required Guid Id { get; init; }
        public required string RemoteDeviceId { get; init; }
        public required string RemoteName { get; init; }
        public string RemotePublicKey { get; init; } = "";
        public required SessionPermissions Permissions { get; set; }
        public required bool IsController { get; init; }
        public required SessionCrypto Crypto { get; init; }
        public required SessionHandshake Handshake { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public TransportKind Transport { get; set; } = TransportKind.Relay;
        public NetworkStream? DirectStream { get; set; }
        public CancellationTokenSource? DirectCts { get; set; }
        public Task? DirectReceiveTask { get; set; }
        public bool ControlOwner { get; set; }
        public bool VideoConfigured { get; set; }
        public ConnectionStateMachine StateMachine { get; } = new();
        public ConnectionHealthMonitor Health { get; } = new();
        public ConcurrentDictionary<string, string> FileDestinations { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, TaskCompletionSource<FileBrowseResponse>> BrowseRequests { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, TaskCompletionSource<FilePullResponse>> PullRequests { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, TaskCompletionSource<FileDirectoryResponse>> DirectoryRequests { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, byte> RequestedPulls { get; } = new(StringComparer.Ordinal);
        public int BrowserBusy;
        public int FilePullBusy;
        public volatile bool AudioEnabled;
        public volatile bool ClipboardEnabled;
        public string? PendingClipboardText;
        public int ClipboardApplyQueued;
        public SystemAudioStream? AudioStream;
        public DateTimeOffset LastHeartbeatSentUtc { get; set; } = DateTimeOffset.UtcNow;

        public string SessionId => Id.ToString("D");
        public ConnectionState State => StateMachine.State;
        public bool HasViewScreen => Permissions.HasFlag(SessionPermissions.ViewScreen);

        public void MarkConnected()
        {
            StateMachine.TryTransition(ConnectionState.Connecting, out _);
            StateMachine.TryTransition(ConnectionState.Authenticating, out _);
            StateMachine.TryTransition(ConnectionState.Connected, out _);
        }

        public void MarkDisconnecting()
        {
            AudioEnabled = ClipboardEnabled = false;
            Interlocked.Exchange(ref PendingClipboardText, null);
            Interlocked.Exchange(ref AudioStream, null)?.Dispose();
            StateMachine.TryTransition(ConnectionState.Disconnecting, out _);
            foreach (var request in BrowseRequests.Values) request.TrySetCanceled();
            foreach (var request in PullRequests.Values) request.TrySetCanceled();
            foreach (var request in DirectoryRequests.Values) request.TrySetCanceled();
            FileDestinations.Clear();
            RequestedPulls.Clear();
        }

        public void MarkDisconnected() =>
            StateMachine.TryTransition(ConnectionState.Disconnected, out _);

        public void Dispose()
        {
            AudioEnabled = ClipboardEnabled = false;
            Interlocked.Exchange(ref PendingClipboardText, null);
            Interlocked.Exchange(ref AudioStream, null)?.Dispose();
            foreach (var request in BrowseRequests.Values) request.TrySetCanceled();
            foreach (var request in PullRequests.Values) request.TrySetCanceled();
            foreach (var request in DirectoryRequests.Values) request.TrySetCanceled();
            FileDestinations.Clear();
            try
            {
                DirectCts?.Cancel();
                DirectStream?.Dispose();
            }
            catch
            {
                // Best effort.
            }

            DirectCts?.Dispose();
            Crypto.Dispose();
            Handshake.Dispose();
        }
    }

    private RemoteSession? GetPrimarySession()
    {
        lock (_sessionLock)
        {
            return _sessions.Values.FirstOrDefault(x => x.IsController)
                   ?? _sessions.Values.FirstOrDefault(x => x.ControlOwner)
                   ?? _sessions.Values.OrderBy(x => x.StartedUtc).FirstOrDefault();
        }
    }

    private void UpdatePrimarySessionAliases()
    {
        RemoteSession? primary;
        SessionInfo[] sessions;
        lock (_sessionLock)
        {
            primary = GetPrimarySession();
            sessions = _sessions.Values
                .OrderBy(x => x.StartedUtc)
                .Select(x => new SessionInfo(
                    x.SessionId,
                    x.RemoteDeviceId,
                    x.RemoteName,
                    x.Permissions,
                    x.IsController,
                    x.Transport,
                    x.StartedUtc))
                .ToArray();
        }

        _crypto = primary is { IsController: true } ? primary.Crypto : null;
        _handshake = primary is { IsController: true } ? primary.Handshake : null;
        _activeSessionId = primary?.SessionId;
        _remoteId = primary?.RemoteDeviceId;
        _remoteName = primary?.RemoteName;
        _permissions = primary?.Permissions ?? SessionPermissions.None;
        _isController = primary?.IsController ?? false;
        _sessionStarted = primary?.StartedUtc;

        _state.Post(() =>
        {
            _state.ActiveSessionCount = sessions.Length;
            _state.ActiveSessionNames = string.Join(", ", sessions.Select(x => x.RemoteName).Distinct());
            if (sessions.Length == 0)
            {
                _state.ResetSession();
                return;
            }

            _state.ActiveSessionId = primary!.SessionId;
            _state.RemoteDeviceId = primary.RemoteDeviceId;
            _state.RemoteDeviceName = sessions.Length == 1
                ? primary.RemoteName
                : $"{sessions.Length} clientes conectados";
            _state.ActivePermissions = primary.Permissions;
            _state.IsController = primary.IsController;
            _state.IsSessionActive = true;
            _state.Transport = primary.Transport;
        });
        _state.NotifySessionChanged();
    }

    private void UpdateSessionManagerLimit()
    {
        _sessionManager.MaxSessions = Math.Clamp(_state.Settings.MaxConcurrentSessions, 1, 100);
    }

    private async Task PersistKnownDeviceAsync(
        string remoteId,
        string remoteName,
        DateTimeOffset startedAt,
        string publicKey)
    {
        try
        {
            var knownDevice = await _knownDevices.RecordConnectionAsync(remoteId, remoteName, startedAt, publicKey)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(publicKey))
            {
                _knownDeviceKeys[remoteId] = publicKey;
            }

            _state.Post(() =>
            {
                for (var index = 0; index < _state.KnownDevices.Count; index++)
                {
                    var current = _state.KnownDevices[index];
                    if (current.DeviceId != remoteId) continue;
                    // Preserve any edits made while the database operation awaited.
                    _state.KnownDevices[index] = current with
                    {
                        IsOnline = true,
                        LastSeenUtc = knownDevice.LastSeenUtc > current.LastSeenUtc ? knownDevice.LastSeenUtc : current.LastSeenUtc,
                        PublicKey = knownDevice.PublicKey
                    };
                    return;
                }
                _state.KnownDevices.Add(knownDevice);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist known device {DeviceId}", remoteId);
        }
    }

    private async Task SendPendingControlAsync(
        IncomingPending pending,
        string type,
        object payload,
        int seconds = 10)
    {
        var signal = _signal;
        if (signal is null || !signal.IsConnected)
        {
            return;
        }

        try
        {
            using var timeout = CreateTimeoutToken(
                TimeSpan.FromSeconds(seconds),
                _lifetimeCts.Token);
            var frame = pending.Crypto.Encrypt(
                SessionFrameKind.Control,
                ControlJson.Create(type, payload));
            await signal.SendBinaryAsync(frame, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                       or System.Net.WebSockets.WebSocketException
                                       or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not send control to pending host session");
        }
    }

    private async Task SendHostControlAsync(
        RemoteSession session,
        string type,
        object payload,
        int seconds = 10)
    {
        await SendEncryptedAsync(
                session,
                SessionFrameKind.Control,
                ControlJson.Create(type, payload),
                seconds)
            .ConfigureAwait(false);
    }

    private async Task SendEncryptedAsync(
        RemoteSession session,
        SessionFrameKind kind,
        byte[] plaintext,
        int seconds = 10,
        CancellationToken cancellationToken = default)
    {
        _ = await SendEncryptedWithResultAsync(session, kind, plaintext, seconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> SendEncryptedWithResultAsync(
        RemoteSession session,
        SessionFrameKind kind,
        byte[] plaintext,
        int seconds = 10,
        CancellationToken cancellationToken = default)
    {
        var signal = _signal;
        if (signal is null || !signal.IsConnected)
        {
            return false;
        }

        try
        {
            var frame = session.Crypto.Encrypt(kind, plaintext);
            if (kind is SessionFrameKind.Video or SessionFrameKind.Audio)
            {
                // Cancelling ClientWebSocket.SendAsync aborts the whole
                // signaling socket. Video frames are droppable, so never use
                // a timeout that could tear down every concurrent session.
                await signal.SendBinaryAsync(frame, cancellationToken).ConfigureAwait(false);
                return true;
            }

            using var timeout = CreateTimeoutToken(
                TimeSpan.FromSeconds(seconds),
                cancellationToken);
            await signal.SendBinaryAsync(frame, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                       or System.Net.WebSockets.WebSocketException
                                       or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not send session frame to {SessionId}", session.SessionId);
            return false;
        }
    }

    private async Task StartHostSessionAsync(RemoteSession session)
    {
        using var sessionScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SessionId"] = session.SessionId,
            ["RemoteDeviceId"] = session.RemoteDeviceId,
            ["RemoteName"] = session.RemoteName,
            ["Role"] = "Host"
        });
        _sessions[session.Id] = session;
        session.MarkConnected();
        if (session.HasViewScreen)
        {
            var viewers = Interlocked.Increment(ref _captureViewerCount);
            if (viewers == 1 || !_captureRunning)
            {
                await StartHostCaptureAsync(0).ConfigureAwait(false);
            }

            await SendMonitorsAsync(session, _lifetimeCts.Token).ConfigureAwait(false);
        }

        await PersistKnownDeviceAsync(
                session.RemoteDeviceId,
                session.RemoteName,
                session.StartedUtc,
                session.RemotePublicKey)
            .ConfigureAwait(false);

        UpdatePrimarySessionAliases();
        _logger.LogInformation(
            LogEvents.SessionStarted,
            "Host session started. SessionId={SessionId} RemoteDeviceId={RemoteDeviceId} RemoteName={RemoteName} Permissions={Permissions} Transport={Transport}",
            session.SessionId,
            session.RemoteDeviceId,
            session.RemoteName,
            session.Permissions,
            session.Transport);
        SessionStarted?.Invoke(this, new SessionInfo(
            session.SessionId,
            session.RemoteDeviceId,
            session.RemoteName,
            session.Permissions,
            IsController: false,
            session.Transport,
            session.StartedUtc));
        _state.Post(() =>
        {
            _state.StatusMessage = session.ControlOwner
                ? global::CSIViewer.Core.Localization.Format("{0} está controlando este equipo.",session.RemoteName)
                : global::CSIViewer.Core.Localization.Format("{0} está viendo este equipo.",session.RemoteName);
        });
        MainWindowSessionBanner?.Invoke(
            _state.ActiveSessionCount > 1
                ? global::CSIViewer.Core.Localization.Format("{0} clientes conectados a este equipo.",_state.ActiveSessionCount)
                : global::CSIViewer.Core.Localization.Format("{0} está conectado a este equipo.",session.RemoteName));
    }

    private RemoteSession? FindSession(Guid sessionId)
        => _sessions.TryGetValue(sessionId, out var session) ? session : null;

    private void RemoveSessionEntry(RemoteSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        _sessionManager.TryRemove(session.Id, out _, out _);
        UpdatePrimarySessionAliases();
    }

    /// <summary>
    /// Drops pending incoming requests without trying to answer the peer.
    /// Used when the server link is lost: a reconnect must never restore an
    /// authorization or a consent decision from the previous connection.
    /// </summary>
    private void ClearPendingIncomingRequests()
    {
        foreach (var entry in _incomingRequests.ToArray())
        {
            if (_incomingRequests.TryRemove(entry.Key, out var pending))
            {
                pending.Cancellation.Cancel();
                pending.Crypto.Dispose();
                pending.Handshake.Dispose();
            }

            _sessionManager.TryRemove(entry.Key, out _, out _);
        }
    }

    private async Task EndSessionByIdAsync(
        Guid sessionId,
        string reason,
        bool sendToPeer,
        ConnectionState finalState)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            await EndRemoteSessionAsync(session, reason, sendToPeer, finalState)
                .ConfigureAwait(false);
        }
    }

    private async Task EndRemoteSessionAsync(
        RemoteSession session,
        string reason,
        bool sendToPeer,
        ConnectionState finalState)
    {
        using var sessionScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SessionId"] = session.SessionId,
            ["RemoteDeviceId"] = session.RemoteDeviceId,
            ["RemoteName"] = session.RemoteName,
            ["Role"] = session.IsController ? "Controller" : "Host"
        });
        await _sessionEndGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessions.TryRemove(session.Id, out _))
            {
                return;
            }

            session.MarkDisconnecting();
            _sessionManager.TryRemove(session.Id, out _, out _);
            // Publish the removed session immediately. Capture/file/history
            // cleanup can await slow I/O; the viewer must stop showing the
            // ended session while that cleanup is still running.
            UpdatePrimarySessionAliases();
            _logger.LogInformation(
                LogEvents.SessionEnded,
                "Session ended. SessionId={SessionId} Role={Role} RemoteDeviceId={RemoteDeviceId} Reason={Reason} FinalState={FinalState}",
                session.SessionId,
                session.IsController ? "Controller" : "Host",
                session.RemoteDeviceId,
                reason,
                finalState);

            // If this session owned mouse/keyboard input, release every key
            // and button that may still be logically pressed on the host.
            if (session.ControlOwner)
            {
                var released = _input.ReleaseAll();
                _logger.LogInformation(
                    "Released {Released} pressed input(s) for control session {SessionId}",
                    released,
                    session.SessionId);
            }

            if (sendToPeer)
            {
                var signal = _signal;
                if (signal is { IsConnected: true })
                {
                    try
                    {
                        using var timeout = CreateTimeoutToken(
                            TimeSpan.FromSeconds(3),
                            _lifetimeCts.Token);
                        await signal.SendAsync(
                                ProtocolJson.Create(
                                    MessageTypes.SessionEnd,
                                    new SessionEndPayload(session.SessionId, reason),
                                    session.SessionId),
                                timeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not send session end");
                    }
                }
            }

            if (!session.IsController && session.HasViewScreen)
            {
                var remaining = Interlocked.Decrement(ref _captureViewerCount);
                if (remaining <= 0)
                {
                    Volatile.Write(ref _captureViewerCount, 0);
                    await StopCaptureIfUnusedAsync().ConfigureAwait(false);
                }
            }

            foreach (var outgoing in _outgoingFiles
                         .Where(x => ReferenceEquals(x.Value.Session, session))
                         .ToArray())
            {
                try
                {
                    outgoing.Value.Cancellation.Cancel();
                }
                catch
                {
                    // Best effort.
                }

                _outgoingFiles.TryRemove(outgoing.Key, out _);
                MarkTransferInterrupted(
                    outgoing.Key,
                    global::CSIViewer.Core.Localization.Text("Conexión perdida. La transferencia se conserva para reanudar."));
            }

            foreach (var incoming in _incomingFiles
                         .Where(x => ReferenceEquals(x.Value.Session, session))
                         .ToArray())
            {
                try
                {
                    // Flush the confirmed bytes and keep the .part + sidecar so
                    // a future offer resumes from the last durable offset.
                    await incoming.Value.Storage.AbortAsync(deletePartial: false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not flush partial transfer {TransferId}", incoming.Key);
                }

                try
                {
                    await incoming.Value.Storage.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best effort.
                }

                _incomingFiles.TryRemove(incoming.Key, out _);
                MarkTransferInterrupted(
                    incoming.Key,
                    global::CSIViewer.Core.Localization.Text("Conexión perdida. La transferencia se conserva para reanudar."));
            }

            var ended = DateTimeOffset.UtcNow;
            var entry = new ConnectionHistoryEntry(
                Guid.NewGuid().ToString("N"),
                session.RemoteDeviceId,
                session.RemoteName,
                session.IsController ? ConnectionDirection.Outgoing : ConnectionDirection.Incoming,
                session.StartedUtc,
                ended,
                ended - session.StartedUtc,
                session.Transport,
                reason.Contains("perdió", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                    ? ConnectionResult.Failed
                    : ConnectionResult.Success,
                reason,
                null);

            try
            {
                if (_state.Settings.StoreConnectionHistory)
                {
                    using var historyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _history.AddAsync(entry, historyTimeout.Token).ConfigureAwait(false);
                    _state.Post(() => _state.History.Insert(0, entry));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not persist session history");
            }

            session.StateMachine.TryTransition(
                finalState == ConnectionState.Failed
                    ? ConnectionState.Failed
                    : ConnectionState.Disconnected,
                out _);
            session.Dispose();
            RefreshClipboardWatcher();

            if (_sessions.IsEmpty)
            {
                SetConnectionState(
                    _state.IsServerOnline ? ConnectionState.Connected : finalState,
                    reason);
            }

            SessionEnded?.Invoke(this, reason);
            MainWindowSessionBanner?.Invoke(
                _state.ActiveSessionCount == 0
                    ? null
                    : global::CSIViewer.Core.Localization.Format("{0} clientes conectados a este equipo.",_state.ActiveSessionCount));
        }
        finally
        {
            _sessionEndGate.Release();
        }
    }

    /// <summary>
    /// Ends every active and pending host session immediately, without
    /// rotating credentials. Used by the host's "Finalizar sesión" action.
    /// </summary>
    public async Task EndAllHostSessionsAsync(string reason = "El usuario finalizó la sesión.")
    {
        _input.ReleaseAll();
        await EndAllSessionsAsync(
                reason,
                sendToPeer: true,
                finalState: _state.IsServerOnline
                    ? ConnectionState.Connected
                    : ConnectionState.Disconnected)
            .ConfigureAwait(false);
        ClearPendingIncomingRequests();
        _input.ReleaseAll();
    }

    /// <summary>
    /// Ends the current role's sessions: the controller's own session, or all
    /// host sessions. Used by every UI "end session" action.
    /// </summary>
    public Task EndCurrentSessionsAsync(string reason = "El usuario finalizó la sesión.")
        => _state.IsController
            ? EndSessionAsync(reason)
            : EndAllHostSessionsAsync(reason);

    /// <summary>
    /// Immediately revokes every active and pending session. Used by the
    /// host's "revoke access" action; it does not silently restore anything
    /// and does not grant permissions to any remaining viewer.
    /// </summary>
    public async Task RevokeAllSessionsAsync(string reason = "Acceso revocado por el usuario.")
    {
        // Reject new incoming requests during the whole revocation window so
        // nobody can authenticate with the old password after sessions close.
        _revokingAccess = true;
        try
        {
            _input.ReleaseAll();
            await EndAllSessionsAsync(
                    reason,
                    sendToPeer: true,
                    finalState: _state.IsServerOnline
                        ? ConnectionState.Connected
                        : ConnectionState.Disconnected)
                .ConfigureAwait(false);
            ClearPendingIncomingRequests();

            // A concurrent authentication task could have activated a session
            // between the previous loop and the pending cleanup. End anything
            // that appeared during the revocation window.
            foreach (var session in _sessions.Values.ToArray())
            {
                await EndRemoteSessionAsync(
                        session,
                        reason,
                        sendToPeer: true,
                        finalState: _state.IsServerOnline
                            ? ConnectionState.Connected
                            : ConnectionState.Disconnected)
                    .ConfigureAwait(false);
            }

            await _identity.RegeneratePasswordAsync().ConfigureAwait(false);
            _input.ReleaseAll();
        }
        finally
        {
            _revokingAccess = false;
        }
    }

    private async Task EndAllSessionsAsync(
        string reason,
        bool sendToPeer,
        ConnectionState finalState)
    {
        var sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
        {
            await EndRemoteSessionAsync(session, reason, sendToPeer, finalState)
                .ConfigureAwait(false);
        }

        foreach (var pending in _incomingRequests.Values.ToArray())
        {
            if (Guid.TryParse(pending.SessionId, out var id))
            {
                await RejectHostRequestAsync(id, reason, "shutdown").ConfigureAwait(false);
            }
        }

        if (_sessions.IsEmpty && _state.ActiveSessionCount == 0)
        {
            SetConnectionState(finalState, reason);
        }
    }

    private async Task StopCaptureIfUnusedAsync()
    {
        if (Volatile.Read(ref _captureViewerCount) != 0)
        {
            return;
        }

        await StopDisplayWatcherAsync().ConfigureAwait(false);
        if (!await _captureGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            _logger.LogWarning("Capture shutdown timed out waiting for the pipeline");
            return;
        }

        try
        {
            if (Volatile.Read(ref _captureViewerCount) != 0)
            {
                return;
            }

            try
            {
                await _modernCapture.StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
                await _capture.StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not stop host capture");
            }
            finally
            {
                _activeCaptureMonitor = null;
                _modernCaptureDisabled = false;
                _captureRunning = false;
            }
        }
        finally
        {
            _captureGate.Release();
        }
    }
}

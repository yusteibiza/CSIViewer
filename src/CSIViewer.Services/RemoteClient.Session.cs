using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Media;
using CSIViewer.Networking;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sessionStartGate = new(1, 1);
    private readonly SemaphoreSlim _sessionEndGate = new(1, 1);
    private readonly SemaphoreSlim _decoderGate = new(1, 1);
    private readonly record struct QueuedVideoFrame(byte[] Data, int Generation);
    private int _videoGeneration;
    private int _videoDecodeFallback;
    private readonly Channel<QueuedVideoFrame> _decodeQueue = Channel.CreateBounded<QueuedVideoFrame>(
        new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            // The decode worker reads normally; configuration/session reset
            // also drains this channel while advancing the generation.
            SingleReader = false,
            SingleWriter = false
        });
    private Task? _decodeLoopTask;
    private double _lastDecodeMilliseconds;

    /// <summary>Duration of the last H.264 decode, measured for diagnostics.</summary>
    public double LastDecodeMilliseconds => Volatile.Read(ref _lastDecodeMilliseconds);
    private readonly object _sessionLock = new();
    private Task? _directAcceptTask;
    private bool _sessionStarting;
    private readonly AuthenticationAttemptTracker _authAttempts = new();
    private readonly SemaphoreSlim _encoderGate = new(1, 1);
    private volatile MonitorInfo? _activeCaptureMonitor;
    private volatile bool _modernCaptureDisabled;
    private volatile bool _captureRunning;
    private int _modernCaptureFallbackScheduled;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly object _displayWatcherGate = new();
    private Task? _displayWatcherTask;
    private CancellationTokenSource? _displayWatcherCts;

    public async Task<Result<bool>> ConnectToPeerAsync(
        string targetId,
        string password,
        SessionPermissions permissions,
        bool chatOnly,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _connectGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Result<bool>.Fail("busy", "Ya hay una conexión en curso.");
        }

        try
        {
            if (_state.IsSessionActive)
            {
                return Result<bool>.Fail("busy", "Ya hay una sesión activa.");
            }

            lock (_sessionLock)
            {
                if (_pendingConnect is not null)
                {
                    return Result<bool>.Fail("pending", "Ya hay una conexión en curso.");
                }
            }

            if (!IsServerOnline)
            {
                try
                {
                    await ConnectToServerAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Result<bool>.Fail(
                        "server_offline",
                        "No se pudo conectar con el servidor: " + ex.GetBaseException().Message);
                }
            }

            var signal = _signal;
            if (!IsServerOnline || signal is null || !signal.IsConnected)
            {
                return Result<bool>.Fail(
                    "server_offline",
                    "El servidor no está disponible. Revisa la configuración.");
            }

            if (_state.Identity is null)
            {
                return Result<bool>.Fail("no_identity", "La identidad del equipo no está disponible.");
            }

            targetId = new string(targetId.Where(char.IsAsciiDigit).ToArray());
            if (targetId.Length != 9)
            {
                return Result<bool>.Fail("invalid_id", "El ID debe tener 9 dígitos.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return Result<bool>.Fail("invalid_password", "Introduce la contraseña temporal.");
            }

            if (chatOnly)
            {
                permissions = SessionPermissions.Chat;
            }
            else if (permissions == SessionPermissions.None)
            {
                permissions = SessionPermissions.ViewScreen |
                              SessionPermissions.ControlInput |
                              SessionPermissions.Chat;
            }

            permissions = SessionPermissionPolicy.Sanitize(permissions);
            var sessionId = Guid.NewGuid().ToString("D");
            var handshake = new SessionHandshake();
            var transcript = SessionHandshake.BuildClientTranscript(
                sessionId,
                _state.DeviceId,
                targetId,
                handshake.PublicKeyBase64,
                handshake.Nonce,
                (int)permissions);
            var signature = Convert.ToBase64String(_state.Identity.Sign(transcript));

            var pending = new PendingConnect
            {
                SessionId = sessionId,
                TargetId = targetId,
                Password = password,
                Permissions = permissions,
                ChatOnly = chatOnly,
                Handshake = handshake,
                ClientTranscript = transcript
            };

            lock (_sessionLock)
            {
                if (_pendingConnect is not null)
                {
                    handshake.Dispose();
                    return Result<bool>.Fail("pending", "Ya hay una conexión en curso.");
                }

                _pendingConnect = pending;
                _handshake = handshake;
                _permissions = permissions;
                _remotePublicKey = null;
                _remoteId = targetId;
                _remoteName = targetId;
            }

            SetConnectionState(ConnectionState.Connecting, $"Solicitando conexión con {targetId}…");
            _state.Post(() => _state.LastError = null);

            try
            {
                using var sendTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), cancellationToken);
                await signal.SendAsync(
                        ProtocolJson.Create(
                            MessageTypes.ConnectRequest,
                            new ConnectRequestPayload(
                                sessionId,
                                targetId,
                                permissions,
                                handshake.PublicKeyBase64,
                                handshake.Nonce,
                                signature,
                                _state.Identity.PublicKeyBase64,
                                chatOnly),
                            sessionId),
                        sendTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await FailPendingConnectAsync("Tiempo de espera al enviar la solicitud.")
                    .ConfigureAwait(false);
                return Result<bool>.Fail("timeout", "Tiempo de espera al enviar la solicitud.");
            }
            catch (Exception ex)
            {
                await FailPendingConnectAsync("No se pudo enviar la solicitud: " + ex.GetBaseException().Message)
                    .ConfigureAwait(false);
                return Result<bool>.Fail("send_failed", ex.GetBaseException().Message);
            }

            try
            {
                return await pending.Completion.Task
                    .WaitAsync(TimeSpan.FromSeconds(65), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await FailPendingConnectAsync("El equipo remoto no respondió a tiempo.")
                    .ConfigureAwait(false);
                return Result<bool>.Fail("timeout", "El equipo remoto no respondió a tiempo.");
            }
            catch (OperationCanceledException)
            {
                await FailPendingConnectAsync("La conexión se canceló.")
                    .ConfigureAwait(false);
                return Result<bool>.Fail("cancelled", "La conexión se canceló.");
            }
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task HandleIncomingConnectAsync(Envelope envelope)
    {
        var payload = ProtocolJson.Payload<ConnectIncomingPayload>(envelope);
        if (payload is null || _state.Identity is null || _signal is null)
        {
            return;
        }

        if (!Guid.TryParse(payload.SessionId, out var sessionGuid))
        {
            return;
        }

        if (!MessageValidation.IsValidDeviceId(payload.FromDeviceId) ||
            !MessageValidation.IsValidName(payload.FromName) ||
            !MessageValidation.IsValidPermissionMask(payload.Permissions) ||
            !MessageValidation.IsValidBase64(payload.EphemeralPublicKey, 32, 1024) ||
            !MessageValidation.IsValidBase64(payload.Nonce, 16, 64) ||
            !MessageValidation.IsValidBase64(payload.Signature, 8, 1024) ||
            !MessageValidation.IsValidBase64(payload.ControllerPublicKey, 32, 1024) ||
            (payload.ChatOnly && payload.Permissions != SessionPermissions.Chat))
        {
            _logger.LogWarning(
                "Rejected malformed connect request from {DeviceId} session {SessionId}",
                payload.FromDeviceId,
                payload.SessionId);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "Solicitud de conexión no válida.",
                    "invalid_request")
                .ConfigureAwait(false);
            return;
        }

        if (_revokingAccess)
        {
            await RejectHostRequestAsync(
                    sessionGuid,
                    "El acceso está siendo revocado. Solicita una nueva contraseña.",
                    "revoked")
                .ConfigureAwait(false);
            return;
        }

        if (!_state.Settings.AllowIncomingConnections)
        {
            await RejectHostRequestAsync(sessionGuid, "El equipo no acepta conexiones entrantes.", "disabled")
                .ConfigureAwait(false);
            return;
        }

        if (payload.ExpiresUtc < DateTimeOffset.UtcNow)
        {
            await RejectHostRequestAsync(sessionGuid, "La solicitud ha caducado.", "expired")
                .ConfigureAwait(false);
            return;
        }

        var clientTranscript = SessionHandshake.BuildClientTranscript(
            payload.SessionId,
            payload.FromDeviceId,
            _state.DeviceId,
            payload.EphemeralPublicKey,
            payload.Nonce,
            (int)payload.Permissions);

        if (!DeviceIdentity.Verify(
                payload.ControllerPublicKey,
                clientTranscript,
                Convert.FromBase64String(payload.Signature)))
        {
            _logger.LogWarning("Rejected connect request with invalid controller signature");
            await RejectHostRequestAsync(
                    sessionGuid,
                    "La identidad del controlador no es válida.",
                    "invalid_signature")
                .ConfigureAwait(false);
            return;
        }

        if (_knownDeviceKeys.TryGetValue(payload.FromDeviceId, out var knownControllerKey) &&
            !DeviceKeyPin.IsConsistent(knownControllerKey, payload.ControllerPublicKey))
        {
            _logger.LogWarning(
                "Controller key changed for device {DeviceId}; refusing session",
                payload.FromDeviceId);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "La clave de identidad del controlador ha cambiado. Verifica la huella.",
                    "controller_key_changed")
                .ConfigureAwait(false);
            return;
        }

        UpdateSessionManagerLimit();
        if (!_sessionManager.TryAddPending(
                sessionGuid,
                payload.FromDeviceId,
                payload.Permissions,
                out var admissionReason))
        {
            var reason = admissionReason switch
            {
                "host_full" => "El equipo alcanzó el límite de clientes simultáneos.",
                "controller_busy" => "Este cliente ya tiene una sesión con el equipo.",
                _ => "La solicitud de conexión no es válida."
            };
            await RejectHostRequestAsync(
                    sessionGuid,
                    reason,
                    admissionReason ?? "admission_rejected")
                .ConfigureAwait(false);
            return;
        }

        SessionHandshake? handshake = null;
        SessionCrypto? crypto = null;
        try
        {
            handshake = new SessionHandshake();
            var finalTranscript = SessionHandshake.BuildFinalTranscript(
                payload.SessionId,
                payload.FromDeviceId,
                _state.DeviceId,
                payload.EphemeralPublicKey,
                handshake.PublicKeyBase64,
                payload.Nonce,
                handshake.Nonce,
                (int)payload.Permissions);

            var signature = Convert.ToBase64String(_state.Identity.Sign(finalTranscript));
            var keys = handshake.DeriveKeys(payload.EphemeralPublicKey, finalTranscript);
            try
            {
                crypto = new SessionCrypto(sessionGuid, isController: false, keys);
            }
            finally
            {
                keys.Clear();
            }

            var pending = new IncomingPending
            {
                SessionId = payload.SessionId,
                FromDeviceId = payload.FromDeviceId,
                FromName = payload.FromName,
                Permissions = payload.Permissions,
                ControllerPublicKey = payload.ControllerPublicKey,
                ControllerEphemeralPublicKey = payload.EphemeralPublicKey,
                ControllerNonce = payload.Nonce,
                ChatOnly = payload.ChatOnly,
                ExpiresUtc = payload.ExpiresUtc,
                Handshake = handshake,
                FinalTranscript = finalTranscript,
                Crypto = crypto
            };

            if (!_incomingRequests.TryAdd(sessionGuid, pending))
            {
                crypto.Dispose();
                handshake.Dispose();
                _sessionManager.TryRemove(sessionGuid, out _, out _);
                await RejectHostRequestAsync(
                        sessionGuid,
                        "La solicitud de conexión está duplicada.",
                        "duplicate_session")
                    .ConfigureAwait(false);
                return;
            }

            _ = ExpireIncomingRequestAsync(sessionGuid, payload.ExpiresUtc);

            var signal = _signal;
            if (signal is null || !signal.IsConnected)
            {
                await RejectHostRequestAsync(
                        sessionGuid,
                        "La conexión con el servidor se perdió.",
                        "server_offline")
                    .ConfigureAwait(false);
                return;
            }

            using var sendTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), _lifetimeCts.Token);
            await signal.SendAsync(
                    ProtocolJson.Create(
                        MessageTypes.ConnectPending,
                        new ConnectPendingPayload(
                            payload.SessionId,
                            handshake.PublicKeyBase64,
                            handshake.Nonce,
                            signature,
                            _state.Identity.PublicKeyBase64,
                            _state.DeviceName,
                            null,
                            null),
                        payload.SessionId),
                    sendTimeout.Token)
                .ConfigureAwait(false);

            _state.Post(() => _state.LastError = null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not answer incoming connection");
            var pendingAdded = _incomingRequests.ContainsKey(sessionGuid);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "No se pudo responder a la solicitud entrante.",
                    "host_error")
                .ConfigureAwait(false);
            if (!pendingAdded)
            {
                crypto?.Dispose();
                handshake?.Dispose();
            }
        }
    }

    private async Task ExpireIncomingRequestAsync(Guid sessionId, DateTimeOffset expiresUtc)
    {
        var delay = expiresUtc - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        try
        {
            await Task.Delay(delay, _lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (!_incomingRequests.TryGetValue(sessionId, out var pending))
        {
            return;
        }

        // Cancel any consent dialog, then serialize with the authentication
        // handler before removing and disposing the pending session.
        pending.Cancellation.Cancel();
        try
        {
            await _incomingAuthGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_incomingRequests.ContainsKey(sessionId))
            {
                await RejectHostRequestAsync(
                        sessionId,
                        "La solicitud de conexión ha caducado.",
                        "expired")
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _incomingAuthGate.Release();
        }
    }

    private async Task RejectHostRequestAsync(Guid sessionGuid, string reason, string code)
    {
        _incomingRequests.TryRemove(sessionGuid, out var pending);
        pending?.Cancellation.Cancel();
        pending?.Crypto.Dispose();
        pending?.Handshake.Dispose();
        _sessionManager.TryRemove(sessionGuid, out _, out _);

        var signal = _signal;
        if (signal is not null && signal.IsConnected)
        {
            try
            {
                using var sendTimeout = CreateTimeoutToken(
                    TimeSpan.FromSeconds(5),
                    _lifetimeCts.Token);
                await signal.SendAsync(
                        ProtocolJson.Create(
                            MessageTypes.ConnectResponse,
                            new ConnectResponsePayload(
                                sessionGuid.ToString("D"),
                                false,
                                SessionPermissions.None,
                                reason),
                            sessionGuid.ToString("D")),
                        sendTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not send host rejection");
            }
        }

        _state.Post(() => _state.LastError = code);
    }

    private async Task HandleConnectPendingAsync(Envelope envelope)
    {
        var payload = ProtocolJson.Payload<ConnectPendingPayload>(envelope);
        PendingConnect? pending;
        lock (_sessionLock)
        {
            pending = _pendingConnect;
        }

        if (payload is null || pending is null || payload.SessionId != pending.SessionId || _state.Identity is null)
        {
            return;
        }

        if (!MessageValidation.IsValidBase64(payload.EphemeralPublicKey, 32, 1024) ||
            !MessageValidation.IsValidBase64(payload.Nonce, 16, 64) ||
            !MessageValidation.IsValidBase64(payload.Signature, 8, 1024) ||
            !MessageValidation.IsValidBase64(payload.HostPublicKey, 32, 1024) ||
            !MessageValidation.IsValidName(payload.HostName) ||
            (payload.DirectEndPoint?.Length ?? 0) > 1024 ||
            (payload.DirectToken?.Length ?? 0) > 1024)
        {
            await FailPendingConnectAsync(
                    "La respuesta del equipo remoto no es válida.",
                    "invalid_session")
                .ConfigureAwait(false);
            return;
        }

        if (_knownDeviceKeys.TryGetValue(pending.TargetId, out var knownHostKey) &&
            !DeviceKeyPin.IsConsistent(knownHostKey, payload.HostPublicKey))
        {
            _logger.LogWarning(
                "Host key changed for device {DeviceId}; refusing automatic connection",
                pending.TargetId);
            await FailPendingConnectAsync(
                    "La clave de identidad del equipo remoto ha cambiado. " +
                    "Verifica la huella o elimina el equipo conocido antes de continuar.",
                    "host_key_changed")
                .ConfigureAwait(false);
            return;
        }

        var transcript = SessionHandshake.BuildFinalTranscript(
            pending.SessionId,
            _state.DeviceId,
            pending.TargetId,
            pending.Handshake.PublicKeyBase64,
            payload.EphemeralPublicKey,
            pending.Handshake.Nonce,
            payload.Nonce,
            (int)pending.Permissions);

        if (!DeviceIdentity.Verify(payload.HostPublicKey, transcript, Convert.FromBase64String(payload.Signature)))
        {
            await FailPendingConnectAsync("La identidad del equipo remoto no es válida.")
                .ConfigureAwait(false);
            return;
        }

        SessionCrypto crypto;
        try
        {
            var keys = pending.Handshake.DeriveKeys(payload.EphemeralPublicKey, transcript);
            try
            {
                crypto = new SessionCrypto(
                    Guid.Parse(pending.SessionId),
                    isController: true,
                    keys);
            }
            finally
            {
                keys.Clear();
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning(ex, "Remote host supplied an invalid ephemeral key");
            await FailPendingConnectAsync(
                    "La clave efímera del equipo remoto no es válida.",
                    "invalid_session")
                .ConfigureAwait(false);
            return;
        }

        lock (_sessionLock)
        {
            if (!ReferenceEquals(_pendingConnect, pending))
            {
                crypto.Dispose();
                return;
            }

            _remotePublicKey = payload.HostPublicKey;
            _remoteName = payload.HostName;
            _handshake = pending.Handshake;
            _crypto = crypto;
            _activeSessionId = pending.SessionId;
        }

        using var sendTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), _lifetimeCts.Token);

        try
        {
            if (_state.Settings.PreferDirectLan)
            {
                using var directTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(5), _lifetimeCts.Token);
                var direct = await DirectSessionClient.TryConnectAsync(
                        payload.DirectEndPoint,
                        payload.DirectToken,
                        pending.SessionId,
                        directTimeout.Token)
                    .ConfigureAwait(false);
                if (direct is not null)
                {
                    AttachDirect(direct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Direct LAN connection unavailable; using relay");
        }

        SetConnectionState(ConnectionState.Authenticating, "Esperando autorización del equipo remoto…");

        try
        {
            await SendControlAsync(
                    "auth.request",
                    new AuthRequestPayload(pending.Password, pending.Permissions),
                    sendTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send authentication request");
            await FailPendingConnectAsync("No se pudo enviar la autenticación al equipo remoto.")
                .ConfigureAwait(false);
        }
    }

    private async Task HandleConnectResultAsync(Envelope envelope)
    {
        var payload = ProtocolJson.Payload<ConnectResultPayload>(envelope);
        if (payload is null)
        {
            return;
        }

        PendingConnect? pending;
        lock (_sessionLock)
        {
            pending = _pendingConnect;
        }

        if (pending is null || payload.SessionId != pending.SessionId)
        {
            return;
        }

        if (!payload.Accepted)
        {
            var reason = !string.IsNullOrWhiteSpace(payload.Reason) &&
                         MessageValidation.IsValidReason(payload.Reason)
                ? payload.Reason
                : "El equipo remoto rechazó la conexión.";
            await FailPendingConnectAsync(reason)
                .ConfigureAwait(false);
            return;
        }

        var grantedPermissions = SessionPermissionPolicy.Sanitize(payload.GrantedPermissions);
        lock (_sessionLock)
        {
            if (!ReferenceEquals(_pendingConnect, pending))
            {
                return;
            }

            _permissions = grantedPermissions;
            _remoteId = payload.RemoteDeviceId;
            _remoteName = payload.RemoteName;
        }

        try
        {
            await StartSessionAsync(
                    payload.SessionId,
                    payload.RemoteDeviceId,
                    payload.RemoteName,
                    grantedPermissions,
                    isController: true)
                .ConfigureAwait(false);

            lock (_sessionLock)
            {
                if (ReferenceEquals(_pendingConnect, pending))
                {
                    _pendingConnect = null;
                }
            }

            pending.Completion.TrySetResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start outgoing session");
            await FailPendingConnectAsync("No se pudo iniciar la sesión remota.")
                .ConfigureAwait(false);
        }
    }

    private async Task RunPendingAuthAsync(byte[] payload, IncomingPending pending)
    {
        try
        {
            await _incomingAuthGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
            try
            {
                await HandleIncomingAuthAsync(payload, pending).ConfigureAwait(false);
            }
            finally
            {
                _incomingAuthGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incoming authentication handling failed");
        }
    }

    private async Task HandleIncomingAuthAsync(byte[] payloadBytes, IncomingPending incoming)
    {
        if (!ControlJson.TryRead(payloadBytes, out var envelope) || envelope is null ||
            envelope.Type != "auth.request")
        {
            return;
        }

        if (!Guid.TryParse(incoming.SessionId, out var sessionGuid))
        {
            return;
        }

        if (!_incomingRequests.ContainsKey(sessionGuid))
        {
            // The server connection was lost while this request was queued.
            return;
        }

        _state.Post(() => _state.StatusMessage = "Verificando contraseña y permisos…");

        var auth = ControlJson.Payload<AuthRequestPayload>(envelope);
        if (auth is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (incoming.ExpiresUtc < now)
        {
            _logger.LogWarning(
                LogEvents.AuthenticationRejected,
                "Rejected expired connection request; device {DeviceId}, session {SessionId}",
                incoming.FromDeviceId,
                incoming.SessionId);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "La solicitud de conexión ha caducado.",
                    "expired")
                .ConfigureAwait(false);
            return;
        }

        var remainingLockout = _authAttempts.RemainingLockout(incoming.FromDeviceId, now);
        if (remainingLockout is not null)
        {
            _logger.LogWarning(
                LogEvents.AuthenticationRateLimited,
                "Authentication rate limited for device {DeviceId}, session {SessionId}, remaining {Remaining}",
                incoming.FromDeviceId,
                incoming.SessionId,
                remainingLockout.Value);
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, "rate_limited"))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "Demasiados intentos fallidos. Espera antes de reintentar.",
                    "rate_limited")
                .ConfigureAwait(false);
            return;
        }

        if (CredentialExpiry.IsExpired(_state.TemporaryPasswordExpiresUtc, now))
        {
            _logger.LogWarning(
                LogEvents.AuthenticationRejected,
                "Rejected authentication with expired temporary password; device {DeviceId}, session {SessionId}",
                incoming.FromDeviceId,
                incoming.SessionId);
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, "password_expired"))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "La contraseña temporal ha caducado. Genera una nueva.",
                    "password_expired")
                .ConfigureAwait(false);
            return;
        }

        if (!MessageValidation.IsValidPassword(auth.Password))
        {
            _authAttempts.RecordFailure(incoming.FromDeviceId, now);
            _logger.LogWarning(
                LogEvents.AuthenticationFailed,
                "Rejected malformed password from device {DeviceId}, session {SessionId}",
                incoming.FromDeviceId,
                incoming.SessionId);
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, "invalid_password"))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(sessionGuid, "Contraseña incorrecta.", "invalid_password")
                .ConfigureAwait(false);
            return;
        }

        var passwordOk = PasswordHasher.Verify(
            _state.UsesCustomPassword
                ? auth.Password
                : TemporaryPasswordGenerator.NormalizeInput(auth.Password),
            _state.TemporaryPasswordSalt,
            _state.TemporaryPasswordHash);

        if (!passwordOk)
        {
            var locked = _authAttempts.RecordFailure(incoming.FromDeviceId, now);
            var rejection = locked ? "rate_limited" : "invalid_password";
            _logger.LogWarning(
                LogEvents.AuthenticationFailed,
                "Authentication failed for device {DeviceId}, session {SessionId}; locked={Locked}",
                incoming.FromDeviceId,
                incoming.SessionId,
                locked);
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, rejection))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(
                    sessionGuid,
                    locked
                        ? "Demasiados intentos fallidos. Espera antes de reintentar."
                        : "Contraseña incorrecta.",
                    rejection)
                .ConfigureAwait(false);
            return;
        }

        _authAttempts.RecordSuccess(incoming.FromDeviceId);
        _logger.LogInformation(
            LogEvents.AuthenticationSucceeded,
            "Authentication succeeded for device {DeviceId}, session {SessionId}",
            incoming.FromDeviceId,
            incoming.SessionId);

        var requestedPermissions = SessionPermissionPolicy.Sanitize(
            auth.Permissions & incoming.Permissions);
        if (requestedPermissions == SessionPermissions.None)
        {
            requestedPermissions = SessionPermissionPolicy.Sanitize(incoming.Permissions);
        }

        if (incoming.ChatOnly)
        {
            requestedPermissions &= SessionPermissions.Chat;
        }

        ConsentDecision decision;
        try
        {
            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Convert.FromBase64String(incoming.ControllerPublicKey)))[..16];
            using var consentCts = CancellationTokenSource.CreateLinkedTokenSource(
                incoming.Cancellation.Token,
                _lifetimeCts.Token);
            decision = await _consent.RequestConsentAsync(
                    new ConsentRequest(
                        incoming.SessionId,
                        incoming.FromDeviceId,
                        incoming.FromName,
                        requestedPermissions,
                        incoming.ChatOnly,
                        fingerprint),
                    consentCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consent UI failed");
            decision = new ConsentDecision(false, SessionPermissions.None);
        }

        if (!_incomingRequests.ContainsKey(sessionGuid))
        {
            return;
        }

        if (_revokingAccess)
        {
            await RejectHostRequestAsync(
                    sessionGuid,
                    "El acceso está siendo revocado.",
                    "revoked")
                .ConfigureAwait(false);
            return;
        }

        var grantedPermissions = SessionPermissionPolicy.Sanitize(
            decision.GrantedPermissions & incoming.Permissions);
        if (incoming.ChatOnly)
        {
            grantedPermissions &= SessionPermissions.Chat;
        }

        if (!decision.Accepted || grantedPermissions == SessionPermissions.None)
        {
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, "denied"))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(sessionGuid, "El usuario rechazó la conexión.", "denied")
                .ConfigureAwait(false);
            return;
        }

        if (grantedPermissions.HasFlag(SessionPermissions.ControlInput) &&
            !grantedPermissions.HasFlag(SessionPermissions.ViewScreen))
        {
            grantedPermissions |= SessionPermissions.ViewScreen;
        }

        if (!_sessionManager.TryActivate(
                sessionGuid,
                grantedPermissions,
                out var effectivePermissions,
                out var controlOwner,
                out var activationReason))
        {
            await SendPendingControlAsync(
                    incoming,
                    "auth.result",
                    new AuthResultPayload(false, SessionPermissions.None, activationReason ?? "not_available"))
                .ConfigureAwait(false);
            await RejectHostRequestAsync(
                    sessionGuid,
                    "La sesión ya no está disponible.",
                    activationReason ?? "not_available")
                .ConfigureAwait(false);
            return;
        }

        effectivePermissions = SessionPermissionPolicy.Sanitize(effectivePermissions);
        if (effectivePermissions.HasFlag(SessionPermissions.ControlInput) &&
            !effectivePermissions.HasFlag(SessionPermissions.ViewScreen))
        {
            effectivePermissions |= SessionPermissions.ViewScreen;
        }

        var session = new RemoteSession
        {
            Id = sessionGuid,
            RemoteDeviceId = incoming.FromDeviceId,
            RemoteName = incoming.FromName,
            RemotePublicKey = incoming.ControllerPublicKey,
            Permissions = effectivePermissions,
            IsController = false,
            Crypto = incoming.Crypto,
            Handshake = incoming.Handshake,
            StartedUtc = DateTimeOffset.UtcNow,
            ControlOwner = controlOwner
        };

        _incomingRequests.TryRemove(sessionGuid, out _);
        try
        {
            await SendHostControlAsync(
                    session,
                    "auth.result",
                    new AuthResultPayload(
                        true,
                        effectivePermissions,
                        activationReason))
                .ConfigureAwait(false);

            var signal = _signal;
            if (signal is null || !signal.IsConnected)
            {
                throw new IOException("La conexión con el servidor se perdió.");
            }

            using var sendTimeout = CreateTimeoutToken(
                TimeSpan.FromSeconds(10),
                _lifetimeCts.Token);
            await signal.SendAsync(
                    ProtocolJson.Create(
                        MessageTypes.ConnectResponse,
                        new ConnectResponsePayload(
                            incoming.SessionId,
                            true,
                            effectivePermissions),
                        incoming.SessionId),
                    sendTimeout.Token)
                .ConfigureAwait(false);

            await StartHostSessionAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not accept incoming session");
            _sessions.TryRemove(session.Id, out _);
            _sessionManager.TryRemove(session.Id, out _, out _);
            session.Dispose();
            await RejectHostRequestAsync(
                    sessionGuid,
                    "No se pudo aceptar la sesión entrante.",
                    "host_error")
                .ConfigureAwait(false);
        }
    }

    private async Task HandleAuthResultAsync(byte[] payloadBytes)
    {
        if (!ControlJson.TryRead(payloadBytes, out var envelope) || envelope is null || envelope.Type != "auth.result")
        {
            return;
        }

        var result = ControlJson.Payload<AuthResultPayload>(envelope);
        PendingConnect? pending;
        lock (_sessionLock)
        {
            pending = _pendingConnect;
        }

        if (result is null || pending is null)
        {
            return;
        }

        if (!result.Accepted)
        {
            var message = result.Reason switch
            {
                "invalid_password" => "Contraseña temporal incorrecta.",
                "password_expired" => "La contraseña temporal ha caducado.",
                "rate_limited" => "Demasiados intentos. Espera antes de reintentar.",
                "denied" => "El usuario rechazó la conexión.",
                _ => "El equipo remoto rechazó la conexión."
            };
            await FailPendingConnectAsync(message, result.Reason ?? "rejected")
                .ConfigureAwait(false);
            return;
        }

        lock (_sessionLock)
        {
            if (!ReferenceEquals(_pendingConnect, pending))
            {
                return;
            }
        }

        try
        {
            await StartSessionAsync(
                    pending.SessionId,
                    pending.TargetId,
                    _remoteName ?? pending.TargetId,
                    SessionPermissionPolicy.Sanitize(result.GrantedPermissions),
                    isController: true)
                .ConfigureAwait(false);

            lock (_sessionLock)
            {
                if (ReferenceEquals(_pendingConnect, pending))
                {
                    _pendingConnect = null;
                }
            }

            pending.Completion.TrySetResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start authenticated session");
            await FailPendingConnectAsync("No se pudo iniciar la sesión remota.")
                .ConfigureAwait(false);
        }
    }

    private async Task FailPendingConnectAsync(string message, string code = "connect_failed")
    {
        await _sessionEndGate.WaitAsync().ConfigureAwait(false);
        try
        {
            PendingConnect? pending;
            lock (_sessionLock)
            {
                pending = _pendingConnect;
                _pendingConnect = null;
                _sessionStarting = false;
            }

            if (pending is null)
            {
                return;
            }

            pending?.Completion.TrySetResult(Result<bool>.Fail(code, message));
            await CleanupPendingSessionAsync().ConfigureAwait(false);
            _state.Post(() => _state.LastError = message);
            SetConnectionState(ConnectionState.Failed, message);
        }
        finally
        {
            _sessionEndGate.Release();
        }
    }

    private async Task StartSessionAsync(
        string sessionId,
        string remoteId,
        string remoteName,
        SessionPermissions permissions,
        bool isController)
    {
        if (!isController)
        {
            // Incoming host sessions are added through StartHostSessionAsync.
            return;
        }

        permissions = SessionPermissionPolicy.Sanitize(permissions);
        await _sessionStartGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
        try
        {
            DateTimeOffset startedAt;
            RemoteSession session;
            lock (_sessionLock)
            {
                if (_sessions.Values.Any(x => x.IsController) || _sessionStarting ||
                    _crypto is null || _handshake is null)
                {
                    return;
                }

                _sessionStarting = true;
                startedAt = DateTimeOffset.UtcNow;
                session = new RemoteSession
                {
                    Id = Guid.Parse(sessionId),
                    RemoteDeviceId = remoteId,
                    RemoteName = remoteName,
                    Permissions = permissions,
                    IsController = true,
                    Crypto = _crypto,
                    Handshake = _handshake,
                    StartedUtc = startedAt,
                    Transport = _transport
                };
                _sessions[session.Id] = session;
                session.MarkConnected();

                _activeSessionId = session.SessionId;
                _remoteId = remoteId;
                _remoteName = remoteName;
                _permissions = permissions;
                _isController = true;
                _sessionStarted = startedAt;
            }

            UpdatePrimarySessionAliases();
            using var sessionScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["SessionId"] = session.SessionId,
                ["RemoteDeviceId"] = session.RemoteDeviceId,
                ["RemoteName"] = session.RemoteName,
                ["Role"] = "Controller"
            });
            SetConnectionState(ConnectionState.Connected, $"Sesión activa con {remoteName}");

            await PersistKnownDeviceAsync(
                    remoteId,
                    remoteName,
                    startedAt,
                    _remotePublicKey ?? "")
                .ConfigureAwait(false);

            if (permissions.HasFlag(SessionPermissions.ViewScreen))
            {
                try
                {
                    using var sendTimeout = CreateTimeoutToken(
                        TimeSpan.FromSeconds(10),
                        _lifetimeCts.Token);
                    await SendControlAsync("video.request_monitors", new { }, sendTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not initialise video for the session");
                }
            }

            SessionStarted?.Invoke(this, new SessionInfo(
                sessionId, remoteId, remoteName, permissions, true, _transport, startedAt));
            _logger.LogInformation(
                LogEvents.SessionStarted,
                "Controller session started. SessionId={SessionId} RemoteDeviceId={RemoteDeviceId} RemoteName={RemoteName} Permissions={Permissions} Transport={Transport}",
                sessionId,
                remoteId,
                remoteName,
                permissions,
                _transport);

            if (SessionPermissionPolicy.CanSendFiles(true, permissions))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var resumed = await ResumeInterruptedTransfersAsync(
                                automatic: true,
                                _lifetimeCts.Token)
                            .ConfigureAwait(false);
                        if (resumed > 0)
                        {
                            _logger.LogInformation(
                                "Auto-resumed {Count} interrupted file transfer(s)",
                                resumed);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Session shutdown.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not auto-resume interrupted file transfers");
                    }
                });
            }

            if (_state.Settings.NotificationsEnabled)
            {
                _notifications.Show("CSIViewer", $"Sesión iniciada con {remoteName}.");
            }

            MainWindowSessionBanner?.Invoke($"Sesión de control con {remoteName}.");
        }
        finally
        {
            lock (_sessionLock)
            {
                _sessionStarting = false;
            }

            _sessionStartGate.Release();
        }
    }

    /// <summary>
    /// True while this process is capturing the local screen for a remote
    /// viewer. Used by the host settings page to explain whether a quality
    /// change was applied immediately.
    /// </summary>
    public bool IsHostCapturing => _captureRunning;

    /// <summary>
    /// Restarts the capture pipeline so quality, FPS and size changes take
    /// effect on an active session. The next connection already reads the
    /// current settings, so this is a no-op when nobody is viewing.
    /// </summary>
    public async Task RestartHostCaptureAsync()
    {
        if (!_captureRunning)
        {
            return;
        }

        await StartHostCaptureAsync(_activeCaptureMonitor?.Index ?? 0).ConfigureAwait(false);
    }

    private async Task StartHostCaptureAsync(int monitorIndex)
    {
        if (!await _captureGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            _logger.LogWarning("Capture restart timed out waiting for the previous pipeline");
            return;
        }

        try
        {
            if (Volatile.Read(ref _captureViewerCount) == 0)
            {
                return;
            }

            _captureRunning = false;
            var monitors = MonitorService.GetMonitors();
            var monitor = monitors.FirstOrDefault(x => x.Index == monitorIndex)
                ?? monitors.FirstOrDefault(x => x.IsPrimary)
                ?? monitors[0];
            _activeCaptureMonitor = monitor;

            try
            {
                await _modernCapture.StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1.5))
                    .ConfigureAwait(false);
                await _capture.StopAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1.5))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Previous capture shutdown timed out");
            }

            _ = Interlocked.Exchange(ref _droppedVideoFrames, 0);
            if (!await _encoderGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                _logger.LogWarning("Could not restart capture: encoder is still busy");
                return;
            }

            try
            {
                _h264Encoder?.Dispose();
                _h264Encoder = null;
                _h264EncoderDisabled = false;
                _h264Width = 0;
                _h264Height = 0;
                foreach (var target in _sessions.Values.Where(x => !x.IsController))
                {
                    target.VideoConfigured = false;
                }
            }
            finally
            {
                _encoderGate.Release();
            }

            var encodeJpeg = !_state.Settings.PreferH264;

            if (_state.Settings.PreferModernCapture &&
                !_modernCaptureDisabled &&
                _modernCapture.TryStart(
                    monitor,
                    _state.Settings.Fps,
                    _state.Settings.ImageQuality,
                    _state.Settings.MaxWidth,
                    encodeJpeg,
                    frame => _ = SendVideoFrameAsync(frame),
                    ex =>
                    {
                        _logger.LogWarning(ex, "Windows.Graphics.Capture error; falling back to GDI");
                        _modernCaptureDisabled = true;
                        _state.Post(() => _state.LastError =
                            "Error de captura moderna; usando captura GDI.");
                        if (Interlocked.Exchange(ref _modernCaptureFallbackScheduled, 1) == 0)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(250, _lifetimeCts.Token).ConfigureAwait(false);
                                    await StartHostCaptureAsync(monitor.Index).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                }
                                finally
                                {
                                    Volatile.Write(ref _modernCaptureFallbackScheduled, 0);
                                }
                            });
                        }
                    }))
            {
                _logger.LogInformation("Using Windows.Graphics.Capture for monitor {Monitor}", monitor.Index);
                _captureRunning = true;
                StartDisplayWatcher();
                return;
            }

            _logger.LogInformation("Using GDI capture fallback for monitor {Monitor}", monitor.Index);
            _capture.Start(
                monitor,
                _state.Settings.Fps,
                _state.Settings.MaxWidth,
                _state.Settings.ImageQuality,
                encodeJpeg,
                frame => _ = SendVideoFrameAsync(frame),
                ex =>
                {
                    _logger.LogWarning(ex, "Capture error");
                    _state.Post(() => _state.LastError = "Error de captura de pantalla.");
                });
            _captureRunning = true;
            StartDisplayWatcher();
        }
        catch (Exception ex)
        {
            _captureRunning = false;
            _logger.LogError(ex, "Could not start capture");
            _state.Post(() => _state.LastError = "No se pudo iniciar la captura de pantalla.");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void StartDisplayWatcher()
    {
        lock (_displayWatcherGate)
        {
            if (_displayWatcherTask is { IsCompleted: false })
            {
                return;
            }

            _displayWatcherCts?.Dispose();
            _displayWatcherCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _displayWatcherTask = Task.Run(
                () => DisplayWatcherLoopAsync(_displayWatcherCts.Token));
        }
    }

    private async Task DisplayWatcherLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _captureViewerCount) == 0)
                {
                    return;
                }

                var monitors = MonitorService.GetMonitors();
                var previous = _activeCaptureMonitor;
                var current = monitors.FirstOrDefault(x => x.Index == (previous?.Index ?? 0))
                    ?? monitors.FirstOrDefault(x => x.IsPrimary)
                    ?? (monitors.Count > 0 ? monitors[0] : null);
                if (current is null)
                {
                    continue;
                }

                var changed = previous is null ||
                              previous.Handle != current.Handle ||
                              previous.Width != current.Width ||
                              previous.Height != current.Height ||
                              previous.X != current.X ||
                              previous.Y != current.Y;
                if (!changed)
                {
                    continue;
                }

                _logger.LogInformation(
                    "Display configuration changed; restarting capture on monitor {Monitor}",
                    current.Index);
                await StartHostCaptureAsync(current.Index).ConfigureAwait(false);
                await SendMonitorsToAllAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Session/capture shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Display change watcher failed");
        }
    }

    private async Task StopDisplayWatcherAsync()
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_displayWatcherGate)
        {
            task = _displayWatcherTask;
            _displayWatcherTask = null;
            cts = _displayWatcherCts;
            _displayWatcherCts = null;
        }

        cts?.Cancel();
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch
            {
                // The watcher is already stopping.
            }
        }

        cts?.Dispose();
    }

    private async Task SendMonitorsToAllAsync(CancellationToken cancellationToken)
    {
        var monitors = MonitorService.GetMonitors()
            .Select(x => new MonitorDescriptor(
                x.Index,
                x.DeviceName,
                x.Width,
                x.Height,
                x.IsPrimary))
            .ToArray();
        var payload = ControlJson.Create(
            "video.monitors",
            new MonitorsPayload(monitors, _activeCaptureMonitor?.Index ?? 0));

        var sends = _sessions.Values
            .Where(x => !x.IsController)
            .Select(session => SendEncryptedAsync(
                session,
                SessionFrameKind.Control,
                payload,
                10,
                cancellationToken))
            .ToArray();
        if (sends.Length > 0)
        {
            await Task.WhenAll(sends).ConfigureAwait(false);
        }
    }

    private Task SendMonitorsAsync(CancellationToken cancellationToken = default)
    {
        var monitors = MonitorService.GetMonitors()
            .Select(x => new MonitorDescriptor(x.Index, x.DeviceName, x.Width, x.Height, x.IsPrimary))
            .ToArray();
        return SendControlAsync(
            "video.monitors",
            new MonitorsPayload(monitors, _activeCaptureMonitor?.Index ?? 0),
            cancellationToken);
    }

    private Task SendMonitorsAsync(
        RemoteSession session,
        CancellationToken cancellationToken = default)
    {
        var monitors = MonitorService.GetMonitors()
            .Select(x => new MonitorDescriptor(x.Index, x.DeviceName, x.Width, x.Height, x.IsPrimary))
            .ToArray();
        return SendEncryptedAsync(
            session,
            SessionFrameKind.Control,
            ControlJson.Create(
                "video.monitors",
                new MonitorsPayload(monitors, _activeCaptureMonitor?.Index ?? 0)),
            10,
            cancellationToken);
    }

    public async Task SelectMonitorAsync(int monitorIndex, CancellationToken cancellationToken = default)
    {
        if (!_state.IsSessionActive || !_isController)
        {
            return;
        }

        await SendControlAsync("video.select_monitor", new SelectMonitorPayload(monitorIndex), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task RequestMonitorsAsync()
        => _state.IsSessionActive && _isController
            ? SendControlWithTimeoutAsync("video.request_monitors", new { })
            : Task.CompletedTask;

    private async Task HandleVideoPayloadAsync(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        if (payload[0] == 0)
        {
            VideoFrameReceived?.Invoke(
                this,
                new VideoFrame(payload[1..], 0, 0, VideoPixelFormat.Jpeg));
            return;
        }

        if (payload[0] != 1)
        {
            return;
        }

        if (_h264Width <= 0 || _h264Height <= 0)
        {
            return;
        }

        // Decoding must never run on the network message loop: a 1440p frame
        // can take tens of milliseconds and would delay input, chat and file
        // messages behind it. Hand the frame to a single background decoder
        // with a bounded backlog. Encoded H.264 frames are dependent: silently
        // dropping an old P-frame corrupts all following reference frames.
        if (Volatile.Read(ref _videoDecodeFallback) != 0)
        {
            return;
        }
        EnsureVideoDecodeLoop();
        if (!_decodeQueue.Writer.TryWrite(new QueuedVideoFrame(payload[1..], Volatile.Read(ref _videoGeneration))))
        {
            _ = Interlocked.Increment(ref _droppedVideoFrames);
            if (Interlocked.Exchange(ref _videoDecodeFallback, 1) == 0)
            {
                _logger.LogWarning("H264 decode backlog full; requesting independent JPEG frames instead of dropping H264 references");
                await SendControlWithTimeoutAsync("video.request_jpeg", new { }).ConfigureAwait(false);
            }
        }
    }

    private void EnsureVideoDecodeLoop()
    {
        if (_decodeLoopTask is { IsCompleted: false })
        {
            return;
        }

        _decodeLoopTask = Task.Run(() => DecodeVideoLoopAsync(_lifetimeCts.Token));
    }

    private async Task DecodeVideoLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var queued in _decodeQueue.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await _decoderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (queued.Generation != Volatile.Read(ref _videoGeneration) ||
                        Volatile.Read(ref _videoDecodeFallback) != 0)
                    {
                        continue;
                    }
                    if (_h264Decoder is null)
                    {
                        if (_h264Width <= 0 || _h264Height <= 0)
                        {
                            continue;
                        }

                        try
                        {
                            _h264Decoder = new H264Decoder(
                                _h264Width,
                                _h264Height,
                                _h264Fps,
                                _state.Settings.ImageQuality);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "H264 decoder creation failed; requesting JPEG");
                            Volatile.Write(ref _videoDecodeFallback, 1);
                            await SendControlWithTimeoutAsync("video.request_jpeg", new { })
                                .ConfigureAwait(false);
                            continue;
                        }
                    }

                    var started = Stopwatch.GetTimestamp();
                    var frames = await _h264Decoder.DecodeAsync(queued.Data).ConfigureAwait(false);
                    Volatile.Write(
                        ref _lastDecodeMilliseconds,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                    foreach (var frame in frames)
                    {
                        if (Volatile.Read(ref _videoDecodeFallback) != 0) break;
                        VideoFrameReceived?.Invoke(this, frame);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "H264 decoder failed; requesting JPEG");
                    Volatile.Write(ref _videoDecodeFallback, 1);
                    _h264Decoder?.Dispose();
                    _h264Decoder = null;
                    await SendControlWithTimeoutAsync("video.request_jpeg", new { })
                        .ConfigureAwait(false);
                }
                finally
                {
                    _decoderGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Video decode loop stopped");
        }
    }

    private async Task SendVideoFrameAsync(CaptureFrame frame)
    {
        // At 90/120 FPS the encoder or the network can be slower than the
        // capture source. Drop the new frame instead of queueing work forever.
        if (Interlocked.Exchange(ref _videoSendBusy, 1) != 0)
        {
            _ = Interlocked.Increment(ref _droppedVideoFrames);
            return;
        }

        try
        {
            await _encoderGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var targets = _sessions.Values
                    .Where(x => !x.IsController && x.HasViewScreen)
                    .ToArray();
            if (targets.Length == 0)
            {
                return;
            }

            try
            {
                var width = frame.Width;
                var height = frame.Height;
                var bgra = frame.Bgra;
                var jpeg = frame.Jpeg;

                if (_state.Settings.PreferH264 && !_h264EncoderDisabled)
                {
                    try
                    {
                        if (bgra.Length == 0 && jpeg is { Length: > 0 })
                        {
                            var decoded = await JpegDecoder.DecodeToBgraAsync(jpeg)
                                .ConfigureAwait(false);
                            bgra = decoded.Bgra;
                            width = decoded.Width;
                            height = decoded.Height;
                        }

                        if (bgra.Length > 0 && width > 0 && height > 0)
                        {
                            if (_h264Encoder is null)
                            {
                                var bitrate = GetVideoBitrate(
                                    width,
                                    height,
                                    _state.Settings.Fps,
                                    _state.Settings.ImageQuality);
                                _h264Encoder = new H264Encoder(
                                    width,
                                    height,
                                    _state.Settings.Fps,
                                    bitrate);
                                _h264Width = width;
                                _h264Height = height;
                                _h264Fps = _state.Settings.Fps;
                                foreach (var target in targets)
                                {
                                    target.VideoConfigured = false;
                                }

                                _logger.LogInformation(
                                    "H264 encoder started at {Width}x{Height} {Fps} FPS, {Bitrate} bps, low latency",
                                    _h264Width, _h264Height, _h264Fps, bitrate);
                            }
                            else if (width != _h264Width || height != _h264Height)
                            {
                                _h264Encoder.Dispose();
                                _h264Encoder = null;
                                _h264EncoderDisabled = true;
                            }

                            if (_h264Encoder is not null)
                            {
                                var h264 = _h264Encoder.Encode(bgra);
                                if (h264.Length > 0)
                                {
                                    var payload = new byte[h264.Length + 1];
                                    payload[0] = 1;
                                    h264.CopyTo(payload, 1);
                                    foreach (var target in targets)
                                    {
                                        if (!target.VideoConfigured)
                                        {
                                            await SendHostControlAsync(
                                                    target,
                                                    "video.config",
                                                    new VideoConfigPayload(
                                                        width,
                                                        height,
                                                        _state.Settings.Fps,
                                                        _state.Settings.ImageQuality))
                                                .ConfigureAwait(false);
                                            target.VideoConfigured = true;
                                        }

                                        await SendEncryptedAsync(
                                                target,
                                                SessionFrameKind.Video,
                                                payload)
                                            .ConfigureAwait(false);
                                    }

                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "H264 encoder failed; falling back to JPEG");
                        _h264Encoder?.Dispose();
                        _h264Encoder = null;
                        _h264EncoderDisabled = true;
                    }
                }

                if (jpeg is null or { Length: 0 })
                {
                    if (bgra.Length == 0 || width <= 0 || height <= 0)
                    {
                        return;
                    }

                    jpeg = await JpegEncoder.EncodeBgraAsync(
                            bgra,
                            width,
                            height,
                            _state.Settings.ImageQuality,
                            _state.Settings.MaxWidth)
                        .ConfigureAwait(false);
                }

                if (jpeg.Length == 0)
                {
                    return;
                }

                var jpegPayload = new byte[jpeg.Length + 1];
                jpegPayload[0] = 0;
                jpeg.CopyTo(jpegPayload, 1);
                foreach (var target in targets)
                {
                    await SendEncryptedAsync(
                            target,
                            SessionFrameKind.Video,
                            jpegPayload)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Dropped video frame");
            }
            }
            finally
            {
                _encoderGate.Release();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _videoSendBusy, 0);
        }
    }

    private static int GetVideoBitrate(int width, int height, int fps, int quality)
    {
        // Scale the bitrate with the real pixel count, frame rate and the
        // quality slider. The old implementation only looked at FPS, so a
        // 2560x1440 stream at the maximum quality still received about
        // 10 Mbps and text looked soft. A bits-per-pixel model keeps small
        // 720p sessions efficient while allowing sharp 1440p desktop text.
        var pixels = (double)Math.Max(16, width) * Math.Max(16, height);
        var normalizedQuality = (Math.Clamp(quality, 20, 95) - 20) / 75.0;
        var bitsPerPixel = 0.035 + normalizedQuality * 0.115;
        var bitrate = pixels * Math.Clamp(fps, 1, 120) * bitsPerPixel;
        return (int)Math.Clamp(bitrate, 1_000_000, 25_000_000);
    }

    private async Task HandleBinaryAsync(byte[] data)
    {
        if (!SessionFrame.TryReadHeader(data, out var sessionGuid, out var headerKind, out _, out _))
        {
            return;
        }

        if (_incomingRequests.TryGetValue(sessionGuid, out var pending))
        {
            if (!pending.Crypto.TryDecrypt(data, out var pendingKind, out var pendingPayload))
            {
                return;
            }

            if (pendingKind == SessionFrameKind.Control)
            {
                await HandleHostControlAsync(pendingPayload, null, pending).ConfigureAwait(false);
            }

            return;
        }

        if (_sessions.TryGetValue(sessionGuid, out var session))
        {
            if (!session.Crypto.TryDecrypt(data, out var kind, out var plaintext))
            {
                if (headerKind == SessionFrameKind.Input &&
                    Interlocked.Increment(ref _inputRejectedLogCounter) % 30 == 1)
                {
                    _logger.LogWarning("Input decrypt rejected (sampled; header unauthenticated). Session={SessionId}", session.SessionId);
                }
                return;
            }

            session.Health.RecordActivity(DateTimeOffset.UtcNow);

            if (session.IsController)
            {
                await HandleControllerFrameAsync(kind, plaintext, session.Permissions, session).ConfigureAwait(false);
            }
            else
            {
                await HandleHostSessionFrameAsync(session, kind, plaintext).ConfigureAwait(false);
            }

            return;
        }

        // The outgoing controller receives auth.result before its session object exists.
        SessionCrypto? crypto;
        lock (_gate)
        {
            crypto = _crypto;
        }

        if (crypto is null || !crypto.TryDecrypt(data, out var fallbackKind, out var fallbackPayload))
        {
            return;
        }

        await HandleControllerFrameAsync(fallbackKind, fallbackPayload, _permissions)
            .ConfigureAwait(false);
    }

    private async Task HandleControllerFrameAsync(
        SessionFrameKind kind,
        byte[] plaintext,
        SessionPermissions permissions,
        RemoteSession? session = null)
    {
        switch (kind)
        {
            case SessionFrameKind.Control:
                await HandleControlAsync(plaintext, session).ConfigureAwait(false);
                break;

            case SessionFrameKind.Video:
                if (SessionPermissionPolicy.CanViewScreen(permissions))
                {
                    await HandleVideoPayloadAsync(plaintext).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Dropped video frame without ViewScreen permission");
                }

                break;

            case SessionFrameKind.Chat:
                if (SessionPermissionPolicy.CanChat(permissions))
                {
                    await HandleIncomingChatAsync(plaintext).ConfigureAwait(false);
                }

                break;

            case SessionFrameKind.Input:
                if (!_isController)
                {
                    var input = System.Text.Json.JsonSerializer.Deserialize<InputEventPayload>(
                        plaintext, ProtocolJson.Options);
                    if (input is not null && MessageValidation.IsValidInput(input))
                    {
                        var monitor = _activeCaptureMonitor;
                        var bounds = monitor is null
                            ? (MonitorBounds?)null
                            : new MonitorBounds(monitor.X, monitor.Y, monitor.Width, monitor.Height);
                        _input.Send(input, bounds);
                    }
                }

                break;

            case SessionFrameKind.FileChunk:
                if (SessionPermissionPolicy.CanTransferFiles(permissions))
                {
                    await HandleFileChunkAsync(plaintext, session).ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task HandleHostSessionFrameAsync(
        RemoteSession session,
        SessionFrameKind kind,
        byte[] plaintext)
    {
        switch (kind)
        {
            case SessionFrameKind.Control:
                await HandleHostControlAsync(plaintext, session, null).ConfigureAwait(false);
                break;

            case SessionFrameKind.Input:
                var input = System.Text.Json.JsonSerializer.Deserialize<InputEventPayload>(
                    plaintext, ProtocolJson.Options);
                var traceInput = input is not null && input.Kind == "mouse" && (input.Action != "move" || input.TraceSampled);
                if (traceInput)
                {
                    _logger.LogInformation("Input received. Trace={TraceId} Session={SessionId} Action={Action} x={X} y={Y} HasPosition={HasPosition}",
                        input!.TraceId, session.SessionId, input.Action, input.X, input.Y, input.HasPosition);
                }
                if (!session.ControlOwner ||
                    !SessionPermissionPolicy.CanControl(session.Permissions))
                {
                    if (traceInput || Interlocked.Increment(ref _inputRejectedLogCounter) % 30 == 0)
                    {
                        _logger.LogWarning(
                            "Input rejected. Trace={TraceId} Session={SessionId} ControlOwner={ControlOwner} Permissions={Permissions}",
                            input?.TraceId, session.SessionId, session.ControlOwner, session.Permissions);
                    }

                    break;
                }

                if (input is not null && MessageValidation.IsValidInput(input))
                {
                    var monitor = _activeCaptureMonitor;
                    var bounds = monitor is null
                        ? (MonitorBounds?)null
                        : new MonitorBounds(monitor.X, monitor.Y, monitor.Width, monitor.Height);
                    var sent = _input.Send(input, bounds, out var diagnostics);
                    if (traceInput || !sent)
                    {
                        _logger.LogInformation("Input injection result. Trace={TraceId} Session={SessionId} Success={Success} Monitor={Monitor} Diagnostics={Diagnostics}",
                            input.TraceId, session.SessionId, sent, bounds, diagnostics);
                    }
                    if (!sent)
                    {
                        _logger.LogWarning(
                            "Input injection failed. Action={Action} x={X} y={Y} Monitor={Monitor} Session={SessionId} ControlOwner={ControlOwner} Permissions={Permissions}",
                            input.Action, input.X, input.Y,
                            monitor is null ? "virtual" : $"{monitor.Index}:{monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height}",
                            session.SessionId,
                            session.ControlOwner,
                            session.Permissions);
                    }
                    else if (input.Action != "move" ||
                             Interlocked.Increment(ref _inputHostLogCounter) % 30 == 0)
                    {
                        _logger.LogInformation(
                            "Input injected. Action={Action} x={X} y={Y} Monitor={Monitor} Session={SessionId}",
                            input.Action, input.X, input.Y,
                            monitor is null ? "virtual" : $"{monitor.Index}:{monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height}",
                            session.SessionId);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Dropped malformed input from session {SessionId}",
                        session.SessionId);
                }

                break;

            case SessionFrameKind.Chat:
                if (SessionPermissionPolicy.CanChat(session.Permissions))
                {
                    await HandleIncomingChatAsync(plaintext, session).ConfigureAwait(false);
                }

                break;

            case SessionFrameKind.FileChunk:
                if (SessionPermissionPolicy.CanTransferFiles(session.Permissions))
                {
                    await HandleFileChunkAsync(plaintext, session).ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task HandleHostControlAsync(
        byte[] payload,
        RemoteSession? session,
        IncomingPending? pending)
    {
        if (!ControlJson.TryRead(payload, out var envelope) || envelope is null)
        {
            return;
        }

        if (pending is not null)
        {
            if (envelope.Type == "auth.request")
            {
                // Do not block the network message loop while the consent
                // dialog is open; other sessions must keep flowing.
                _ = RunPendingAuthAsync(payload, pending);
            }

            return;
        }

        if (session is null)
        {
            return;
        }

        if (TryHandleFileBrowserControl(envelope, session)) return;

        switch (envelope.Type)
        {
            case "session.ping":
                try
                {
                    await SendHostControlAsync(
                            session,
                            "session.pong",
                            ControlJson.Payload<PingPayload>(envelope)
                                ?? new PingPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not answer session heartbeat");
                }

                break;

            case "session.pong":
                break;

            case "video.request_monitors":
            {
                using var sendTimeout = CreateTimeoutToken(
                    TimeSpan.FromSeconds(10),
                    _lifetimeCts.Token);
                await SendMonitorsAsync(session, sendTimeout.Token).ConfigureAwait(false);
                break;
            }

            case "video.select_monitor":
            {
                if (!session.ControlOwner)
                {
                    break;
                }

                var request = ControlJson.Payload<SelectMonitorPayload>(envelope);
                if (request is not null && request.Index is >= 0 and < 32)
                {
                    await StartHostCaptureAsync(request.Index).ConfigureAwait(false);
                }

                break;
            }

            case "video.request_jpeg":
                if (!await _encoderGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                {
                    break;
                }

                try
                {
                    _h264Encoder?.Dispose();
                    _h264Encoder = null;
                    _h264EncoderDisabled = true;
                    foreach (var target in _sessions.Values.Where(x => !x.IsController))
                    {
                        target.VideoConfigured = false;
                    }
                }
                finally
                {
                    _encoderGate.Release();
                }

                break;

            case "file.offer":
                await HandleFileOfferAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.accept":
                await HandleFileAcceptAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.result":
                await HandleFileResultAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.cancel":
                await HandleFileCancelAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.pause":
                await HandleFilePauseAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.directory":
                await HandleFileDirectoryAsync(envelope, session).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleControlAsync(byte[] payload, RemoteSession? session = null)
    {
        if (!ControlJson.TryRead(payload, out var envelope) || envelope is null)
        {
            return;
        }

        if (TryHandleFileBrowserControl(envelope, session)) return;

        switch (envelope.Type)
        {
            case "auth.result":
                // The encrypted auth.result can arrive before the JSON
                // ConnectResult that creates the controller session.
                if (_isController || _pendingConnect is not null)
                {
                    await HandleAuthResultAsync(payload).ConfigureAwait(false);
                }

                break;

            case "session.ping":
                try
                {
                    await SendControlAsync(
                            "session.pong",
                            ControlJson.Payload<PingPayload>(envelope)
                                ?? new PingPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                            _lifetimeCts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not answer session heartbeat");
                }

                break;

            case "session.pong":
                break;

            case "video.monitors":
            {
                if (_isController)
                {
                    var monitors = ControlJson.Payload<MonitorsPayload>(envelope);
                    if (monitors is not null &&
                        MessageValidation.IsValidMonitors(monitors.Monitors))
                    {
                        MonitorsReceived?.Invoke(this, monitors);
                    }
                }

                break;
            }

            case "video.request_monitors":
                if (!_isController)
                {
                    using var sendTimeout = CreateTimeoutToken(
                        TimeSpan.FromSeconds(10),
                        _lifetimeCts.Token);
                    await SendMonitorsAsync(sendTimeout.Token).ConfigureAwait(false);
                }

                break;

            case "video.select_monitor":
                if (!_isController)
                {
                    var request = ControlJson.Payload<SelectMonitorPayload>(envelope);
                    if (request is not null && request.Index is >= 0 and < 32)
                    {
                        await StartHostCaptureAsync(request.Index).ConfigureAwait(false);
                    }
                }

                break;

            case "video.config":
                if (_isController)
                {
                    var config = ControlJson.Payload<VideoConfigPayload>(envelope);
                    if (config is not null && MessageValidation.IsValidVideoConfig(config))
                    {
                        await _decoderGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
                        try
                        {
                            Interlocked.Increment(ref _videoGeneration);
                            Volatile.Write(ref _videoDecodeFallback, 0);
                            _h264Decoder?.Dispose();
                            _h264Decoder = null;
                            _h264Width = config.Width;
                            _h264Height = config.Height;
                            _h264Fps = config.Fps;
                            while (_decodeQueue.Reader.TryRead(out _))
                            {
                                // Drop frames from the previous monitor size.
                            }

                            try
                            {
                                _h264Decoder = new H264Decoder(
                                    config.Width, config.Height, config.Fps, _state.Settings.ImageQuality);
                                _logger.LogInformation("H264 decoder configured at {Width}x{Height}", config.Width, config.Height);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "H264 decoder initialisation failed; requesting JPEG");
                                await SendControlAsync("video.request_jpeg", new { }).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            _decoderGate.Release();
                        }
                    }
                }

                break;

            case "video.request_jpeg":
                if (!_isController)
                {
                    if (!await _encoderGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                    {
                        break;
                    }

                    try
                    {
                        _h264Encoder?.Dispose();
                        _h264Encoder = null;
                        _h264EncoderDisabled = true;
                    }
                    finally
                    {
                        _encoderGate.Release();
                    }
                }

                break;

            case "file.offer":
                await HandleFileOfferAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.accept":
                await HandleFileAcceptAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.result":
                await HandleFileResultAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.cancel":
                await HandleFileCancelAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.pause":
                await HandleFilePauseAsync(envelope, session).ConfigureAwait(false);
                break;

            case "file.directory":
                await HandleFileDirectoryAsync(envelope, session).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// True only while a real controller session exists, is connected and
    /// still has the control permission. AppState can briefly lag behind a
    /// session teardown; input must never rely only on that cached state.
    /// </summary>
    private long _droppedInputFrames;
    private int _inputLogCounter;
    private int _inputHostLogCounter;
    private int _inputRejectedLogCounter;

    public long DroppedInputFrames => Interlocked.Read(ref _droppedInputFrames);

    public bool CanControlRemote
    {
        get
        {
            var session = _sessions.Values.FirstOrDefault(x => x.IsController);
            return session is not null &&
                   session.State == ConnectionState.Connected &&
                   SessionPermissionPolicy.CanControl(session.Permissions);
        }
    }

    public async Task SendInputAsync(InputEventPayload input, CancellationToken cancellationToken = default)
    {
        var traceInput = input.Kind == "mouse" && (input.Action != "move" || input.TraceSampled);
        var loggedAction = input.Kind == "text" ? "[redacted text]" : input.Action;
        var session = _sessions.Values.FirstOrDefault(x => x.IsController);
        if (session is null ||
            session.State != ConnectionState.Connected ||
            !SessionPermissionPolicy.CanControl(session.Permissions) ||
            !MessageValidation.IsValidInput(input))
        {
            _ = Interlocked.Increment(ref _droppedInputFrames);
            if (traceInput ||
                Interlocked.Increment(ref _inputLogCounter) % 30 == 0)
            {
                _logger.LogWarning(
                    "Input dropped before send. Trace={TraceId} Action={Action} x={X} y={Y} Session={SessionState} Permissions={Permissions} SessionFound={Found}",
                    input.TraceId, loggedAction, input.X, input.Y,
                    session?.State.ToString() ?? "none",
                    session?.Permissions.ToString() ?? "none",
                    session is not null);
            }

            return;
        }

        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input, ProtocolJson.Options);
        var transportSent = false;
        try
        {
            transportSent = await SendEncryptedWithResultAsync(session, SessionFrameKind.Input, payload, 10, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Input send failed. Trace={TraceId} Action={Action} Session={SessionId}",
                input.TraceId, loggedAction,
                session.SessionId);
            return;
        }

        if (traceInput || !transportSent)
        {
            _logger.LogInformation(
                "Input transport result. Trace={TraceId} Action={Action} x={X} y={Y} Session={SessionId} Transport=Relay Written={Written} (not a host acknowledgement)",
                input.TraceId, loggedAction, input.X, input.Y, session.SessionId, transportSent);
        }
    }

    private async Task SendEncryptedAsync(SessionFrameKind kind, byte[] plaintext, CancellationToken cancellationToken = default)
    {
        SessionCrypto? crypto;
        SignalingClient? signal;
        NetworkStream? direct;
        lock (_gate)
        {
            crypto = _crypto;
        }

        lock (_connectionLock)
        {
            signal = _signal;
        }

        lock (_directGate)
        {
            direct = _directStream;
        }

        if (crypto is null)
        {
            return;
        }

        var frame = crypto.Encrypt(kind, plaintext);

        if (direct is not null)
        {
            try
            {
                using var directTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), cancellationToken);
                await _directSendLock.WaitAsync(directTimeout.Token).ConfigureAwait(false);
                try
                {
                    await FrameProtocol.WriteFrameAsync(direct, frame, directTimeout.Token)
                        .ConfigureAwait(false);
                    return;
                }
                finally
                {
                    _directSendLock.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                                           or OperationCanceledException)
            {
                _logger.LogDebug(ex, "Direct LAN send failed; falling back to relay");
                DetachDirect(direct);
            }
        }

        if (signal is null || !signal.IsConnected)
        {
            return;
        }

        try
        {
            using var relayTimeout = CreateTimeoutToken(TimeSpan.FromSeconds(10), cancellationToken);
            await signal.SendBinaryAsync(frame, relayTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or System.Net.WebSockets.WebSocketException
                                       or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Signaling send failed");
        }
    }

    private async Task AcceptDirectAsync(DirectSessionServer server, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await server.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (stream is not null)
            {
                AttachDirect(stream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct LAN accept failed");
        }
    }

    private void AttachDirect(NetworkStream stream)
    {
        try
        {
            stream.Socket.NoDelay = true;
        }
        catch
        {
            // NoDelay is an optimization.
        }

        CancellationToken token;
        lock (_directGate)
        {
            if (_directStream is not null)
            {
                stream.Dispose();
                return;
            }

            _directStream = stream;
            _transport = TransportKind.LanDirect;
            _state.Post(() => _state.Transport = TransportKind.LanDirect);
            _directCts?.Cancel();
            _directCts?.Dispose();
            _directCts = new CancellationTokenSource();
            token = _directCts.Token;
        }

        _directReceiveTask = ReceiveDirectAsync(stream, token);
        _logger.LogInformation("Direct LAN session transport established");
        _state.Post(() => _state.StatusMessage = "Conexión directa LAN establecida.");
    }

    private async Task ReceiveDirectAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await FrameProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    break;
                }

                RecordSessionActivityFromFrame(frame);
                Enqueue(new ReceivedMessage(
                    ReceivedMessageKind.DirectBinary,
                    null,
                    null,
                    frame,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct LAN receive failed; falling back to relay");
        }
        finally
        {
            DetachDirect(stream);
        }
    }

    private void DetachDirect(NetworkStream? expected = null)
    {
        NetworkStream? stream;
        lock (_directGate)
        {
            if (expected is not null && !ReferenceEquals(_directStream, expected))
            {
                return;
            }

            stream = _directStream;
            _directStream = null;
            _transport = TransportKind.Relay;
            _state.Post(() => _state.Transport = TransportKind.Relay);
        }

        try
        {
            stream?.Dispose();
        }
        catch
        {
            // Best effort.
        }
    }

    private Task SendControlAsync(string type, object payload, CancellationToken cancellationToken = default)
        => SendEncryptedAsync(SessionFrameKind.Control, ControlJson.Create(type, payload), cancellationToken);

    private async Task SendControlWithTimeoutAsync(
        string type,
        object payload,
        int seconds = 10)
    {
        using var timeout = CreateTimeoutToken(
            TimeSpan.FromSeconds(seconds),
            _lifetimeCts.Token);
        await SendControlAsync(type, payload, timeout.Token).ConfigureAwait(false);
    }

    public async Task EndSessionAsync(string reason = "El usuario finalizó la sesión.")
    {
        var primary = GetPrimarySession();
        if (primary is null)
        {
            SetConnectionState(
                _state.IsServerOnline ? ConnectionState.Connected : ConnectionState.Disconnected,
                reason);
            return;
        }

        await EndRemoteSessionAsync(
                primary,
                reason,
                sendToPeer: true,
                _state.IsServerOnline ? ConnectionState.Connected : ConnectionState.Disconnected)
            .ConfigureAwait(false);
    }

    private async Task CleanupPendingSessionAsync()
    {
        CancellationTokenSource? directCts;
        Task? directAcceptTask;
        Task? directReceiveTask;
        DirectSessionServer? directServer;
        NetworkStream? directStream;
        PendingConnect? pending;
        bool hasHostSessions;

        lock (_sessionLock)
        {
            hasHostSessions = _sessions.Values.Any(x => !x.IsController);
            directCts = _directCts;
            _directCts = null;
            directAcceptTask = _directAcceptTask;
            _directAcceptTask = null;
            directReceiveTask = _directReceiveTask;
            _directReceiveTask = null;
            directServer = _directServer;
            _directServer = null;
            directStream = _directStream;
            _directStream = null;
            pending = _pendingConnect;
            _pendingConnect = null;
            _activeSessionId = null;
            _sessionStarted = null;
        }

        directCts?.Cancel();
        try
        {
            directStream?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        var cleanupTasks = new List<Task>(2);
        if (directAcceptTask is not null)
        {
            cleanupTasks.Add(directAcceptTask);
        }

        if (directReceiveTask is not null)
        {
            cleanupTasks.Add(directReceiveTask);
        }

        if (cleanupTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(cleanupTasks)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Direct transport tasks are already stopping.
            }
        }

        directCts?.Dispose();

        if (directServer is not null)
        {
            try
            {
                await directServer.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
            catch
            {
                // The listener is already closed.
            }
        }

        pending?.Completion.TrySetResult(Result<bool>.Fail(
            "cancelled",
            "La sesión se canceló."));

        _transport = TransportKind.Relay;
        _state.Post(() => _state.Transport = TransportKind.Relay);

        if (!hasHostSessions)
        {
            var encoderAcquired = false;
            try
            {
                encoderAcquired = await _encoderGate
                    .WaitAsync(TimeSpan.FromSeconds(5), _lifetimeCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown is canceling the session.
            }

            if (encoderAcquired)
            {
                try
                {
                    _h264Encoder?.Dispose();
                    _h264Encoder = null;
                }
                finally
                {
                    _encoderGate.Release();
                }
            }

            try
            {
                await _decoderGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
                try
                {
                    Interlocked.Increment(ref _videoGeneration);
                    Volatile.Write(ref _videoDecodeFallback, 0);
                    _h264Decoder?.Dispose();
                    _h264Decoder = null;
                    while (_decodeQueue.Reader.TryRead(out _))
                    {
                        // Discard frames from the session that just ended.
                    }
                }
                finally
                {
                    _decoderGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown is canceling the session.
            }

            _h264EncoderDisabled = false;
            _h264Width = 0;
            _h264Height = 0;
        }

        lock (_gate)
        {
            _crypto?.Dispose();
            _crypto = null;
        }

        SessionHandshake? handshake;
        lock (_sessionLock)
        {
            handshake = _handshake;
            _handshake = null;
        }

        handshake?.Dispose();

        foreach (var outgoing in _outgoingFiles
                     .Where(x => x.Value.Session is null)
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
        }

        foreach (var file in _incomingFiles
                     .Where(x => x.Value.Session is null)
                     .ToArray())
        {
            try
            {
                await file.Value.Storage.AbortAsync(deletePartial: false).ConfigureAwait(false);
                await file.Value.Storage.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best effort cleanup.
            }

            _incomingFiles.TryRemove(file.Key, out _);
        }

        UpdatePrimarySessionAliases();
    }

    public static Action<string?>? MainWindowSessionBanner { get; set; }
}

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;
using CSIViewer.Protocol;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private const int WebSocketConnectTimeoutSeconds = 10;
    private const int AuthenticationTimeoutSeconds = 10;
    private const int MessageTimeoutSeconds = 60;
    private const int ShutdownTimeoutSeconds = 5;

    private readonly ConnectionStateMachine _connectionStateMachine = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _serverGate = new(1, 1);
    private readonly object _connectionLock = new();
    private readonly object _reconnectGate = new();
    private readonly Channel<ReceivedMessage> _receiveQueue =
        Channel.CreateBounded<ReceivedMessage>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private Task? _messageLoopTask;
    private Task? _reconnectTask;
    private CancellationTokenSource? _reconnectCts;
    private TaskCompletionSource<bool>? _authCompletion;
    private int _disposed;
    private volatile bool _stopping;
    private long _droppedInboundMessages;
    private string? _lastReconnectFailure;

    private enum ReceivedMessageKind
    {
        Envelope,
        Binary,
        DirectBinary,
        Closed
    }

    private readonly record struct ReceivedMessage(
        ReceivedMessageKind Kind,
        SignalingClient? Signal,
        Envelope? Envelope,
        byte[]? Data,
        string? Reason);

    private void StartBackgroundProcessing()
    {
        _messageLoopTask ??= Task.Run(() => ProcessMessagesAsync(_lifetimeCts.Token));
    }

    private void Enqueue(ReceivedMessage message)
    {
        if (_receiveQueue.Writer.TryWrite(message))
        {
            return;
        }

        _ = Interlocked.Increment(ref _droppedInboundMessages);
        _logger.LogDebug("Inbound message dropped because the queue is full");
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _receiveQueue.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(
                    Math.Clamp(_state.Settings.MessageTimeoutSeconds, 10, 300)));
                try
                {
                    switch (message.Kind)
                    {
                        case ReceivedMessageKind.Envelope when message.Envelope is not null:
                            if (IsCurrentSignal(message.Signal))
                            {
                                await HandleEnvelopeAsync(message.Envelope).ConfigureAwait(false);
                            }

                            break;

                        case ReceivedMessageKind.Binary when message.Data is not null:
                            if (IsCurrentSignal(message.Signal))
                            {
                                await HandleBinaryAsync(message.Data).ConfigureAwait(false);
                            }

                            break;

                        case ReceivedMessageKind.DirectBinary when message.Data is not null:
                            await HandleBinaryAsync(message.Data).ConfigureAwait(false);
                            break;

                        case ReceivedMessageKind.Closed:
                            await HandleServerConnectionClosedAsync(message.Signal, message.Reason ?? global::CSIViewer.Core.Localization.Text("Conexión cerrada"))
                                .ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Timeout processing inbound network message");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to handle inbound network message");
                    _state.Post(() => _state.LastError = global::CSIViewer.Core.Localization.Text("Error procesando un mensaje de red."));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inbound message loop failed");
        }
    }

    private bool HasSessionOrPendingStart()
    {
        lock (_sessionLock)
        {
            return _sessionStarted is not null || _sessionStarting;
        }
    }

    private bool IsCurrentSignal(SignalingClient? signal)
    {
        if (signal is null)
        {
            return false;
        }

        lock (_connectionLock)
        {
            return ReferenceEquals(_signal, signal);
        }
    }

    private void OnEnvelopeReceived(object? sender, Envelope envelope)
    {
        if (sender is SignalingClient signal && IsCurrentSignal(signal))
        {
            _signalingHealth.RecordActivity(DateTimeOffset.UtcNow);
        }

        Enqueue(new ReceivedMessage(
            ReceivedMessageKind.Envelope,
            sender as SignalingClient,
            envelope,
            null,
            null));
    }

    private void OnBinaryReceived(object? sender, byte[] data)
    {
        if (sender is SignalingClient signal && IsCurrentSignal(signal))
        {
            _signalingHealth.RecordActivity(DateTimeOffset.UtcNow);
            RecordSessionActivityFromFrame(data);
        }

        Enqueue(new ReceivedMessage(
            ReceivedMessageKind.Binary,
            sender as SignalingClient,
            null,
            data,
            null));
    }

    /// <summary>
    /// Records session liveness when the frame is received, not only when the
    /// message loop processes it. A slow consent prompt or handler must not
    /// make an otherwise active session look dead.
    /// </summary>
    private void RecordSessionActivityFromFrame(byte[] data)
    {
        if (SessionFrame.TryReadHeader(data, out var sessionId, out _, out _, out _) &&
            _sessions.TryGetValue(sessionId, out var session))
        {
            session.Health.RecordActivity(DateTimeOffset.UtcNow);
        }
    }

    private void OnConnectionClosed(object? sender, string reason)
    {
        Enqueue(new ReceivedMessage(
            ReceivedMessageKind.Closed,
            sender as SignalingClient,
            null,
            null,
            reason));
    }

    private void OnSignalingError(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Signaling error");
    }

    public async Task ConnectToServerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _serverGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopping)
            {
                return;
            }

            lock (_connectionLock)
            {
                if (_signal is { IsConnected: true } && _state.IsServerOnline)
                {
                    return;
                }
            }

            ServerEndpointSecurity.EnsureAllowed(
                _state.Settings.ServerUrl,
                _state.Settings.AllowInsecureServer);

            await _identity.EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
            if (_stopping)
            {
                return;
            }

            await DisposeSignalAsync().ConfigureAwait(false);

            var signal = new SignalingClient();
            signal.EnvelopeReceived += OnEnvelopeReceived;
            signal.BinaryReceived += OnBinaryReceived;
            signal.ConnectionClosed += OnConnectionClosed;
            signal.Error += OnSignalingError;

            var authCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_connectionLock)
            {
                _signal = signal;
                _authCompletion = authCompletion;
            }

            if (!HasSessionOrPendingStart())
            {
                SetConnectionState(ConnectionState.Connecting, global::CSIViewer.Core.Localization.Text("Conectando con el servidor…"));
            }

            _state.Post(() =>
            {
                _state.IsServerOnline = false;
                _state.ServerStatus = global::CSIViewer.Core.Localization.Text("Conectando…");
                _state.StatusMessage = global::CSIViewer.Core.Localization.Text("Conectando con el servidor…");
            });

            try
            {
                using var connectTimeout = CreateTimeoutToken(
                    TimeSpan.FromSeconds(Math.Clamp(_state.Settings.ConnectTimeoutSeconds, 3, 60)),
                    cancellationToken);
                await signal.ConnectAsync(
                        ServerApiClient.BuildWebSocketUri(_state.Settings.ServerUrl),
                        connectTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(global::CSIViewer.Core.Localization.Text("El servidor no respondió al conectar."));
            }

            if (_stopping)
            {
                await DisposeSignalAsync(signal).ConfigureAwait(false);
                return;
            }

            if (!HasSessionOrPendingStart())
            {
                SetConnectionState(ConnectionState.Authenticating, global::CSIViewer.Core.Localization.Text("Autenticando con el servidor…"));
            }

            _state.Post(() => _state.ServerStatus = "Autenticando…");

            bool authenticated;
            try
            {
                using var authTimeout = CreateTimeoutToken(
                    TimeSpan.FromSeconds(Math.Clamp(_state.Settings.ConnectTimeoutSeconds, 3, 60)),
                    cancellationToken);
                authenticated = await authCompletion.Task.WaitAsync(authTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(global::CSIViewer.Core.Localization.Text("El servidor no respondió a la autenticación."));
            }

            if (!authenticated)
            {
                throw new InvalidOperationException(global::CSIViewer.Core.Localization.Text("El servidor rechazó la autenticación."));
            }

            if (_stopping)
            {
                await DisposeSignalAsync(signal).ConfigureAwait(false);
                return;
            }

            _signalingHealth.RecordActivity(DateTimeOffset.UtcNow);
            _lastSignalingPingUtc = DateTimeOffset.MinValue;
            _logger.LogInformation(
                LogEvents.ConnectionOpened,
                "Conexión con el servidor establecida. Url={ServerUrl}",
                _state.Settings.ServerUrl);

            var wasDisconnected = _disconnectedNotified;
            _disconnectedNotified = false;
            _state.Post(() =>
            {
                _state.IsServerOnline = true;
                _state.ServerStatus = global::CSIViewer.Core.Localization.Text("En línea");
                _state.StatusMessage = wasDisconnected
                    ? global::CSIViewer.Core.Localization.Text("Conexión restablecida.")
                    : global::CSIViewer.Core.Localization.Text("Conectado. Disponible para recibir asistencia.");
                _state.LastError = null;
                _state.IsReconnecting = false;
                _state.ReconnectAttempt = 0;
                _state.ReconnectMaxRetries = 0;
            });

            if (wasDisconnected && _state.Settings.NotificationsEnabled)
            {
                _notifications.Show(global::CSIViewer.Core.Localization.Text("Conexión restablecida"), global::CSIViewer.Core.Localization.Text("El servidor vuelve a estar disponible."));
            }

            if (!HasSessionOrPendingStart())
            {
                SetConnectionState(ConnectionState.Connected, global::CSIViewer.Core.Localization.Text("Conectado al servidor."));
            }
        }
        catch (Exception ex)
        {
            await DisposeSignalAsync().ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && !_stopping)
            {
                _logger.LogWarning(
                    LogEvents.ConnectionClosed,
                    ex,
                    "No se pudo conectar con el servidor. Url={ServerUrl}",
                    _state.Settings.ServerUrl);
                _state.Post(() =>
                {
                    _state.IsServerOnline = false;
                    _state.ServerStatus = global::CSIViewer.Core.Localization.Text("Servidor no disponible");
                    _state.StatusMessage = global::CSIViewer.Core.Localization.Text("No se pudo conectar con el servidor.");
                    _state.LastError = ex.GetBaseException().Message;
                });

                if (!HasSessionOrPendingStart() &&
                    _connectionStateMachine.State is ConnectionState.Connecting or ConnectionState.Authenticating)
                {
                    SetConnectionState(ConnectionState.Failed, global::CSIViewer.Core.Localization.Text("No se pudo conectar con el servidor."));
                }
            }

            throw;
        }
        finally
        {
            _serverGate.Release();
        }
    }

    public async Task DisconnectFromServerAsync(CancellationToken cancellationToken = default)
    {
        await CancelReconnectLoopAsync().ConfigureAwait(false);
        await _serverGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.IsEmpty)
            {
                await EndAllSessionsAsync(
                        global::CSIViewer.Core.Localization.Text("Se cerró la conexión con el servidor."),
                        sendToPeer: false,
                        finalState: ConnectionState.Disconnected)
                    .ConfigureAwait(false);
            }

            await DisposeSignalAsync().ConfigureAwait(false);
            _state.Post(() =>
            {
                _state.IsServerOnline = false;
                _state.ServerStatus = global::CSIViewer.Core.Localization.Text("Sin conexión");
                _state.IsReconnecting = false;
                _state.ReconnectAttempt = 0;
            });

            if (!HasSessionOrPendingStart())
            {
                SetConnectionState(ConnectionState.Disconnected, global::CSIViewer.Core.Localization.Text("Desconectado del servidor."));
            }
        }
        finally
        {
            _serverGate.Release();
        }
    }

    private async Task HandleServerConnectionClosedAsync(SignalingClient? signal, string reason)
    {
        if (signal is null || !IsCurrentSignal(signal))
        {
            return;
        }

        _logger.LogWarning(LogEvents.ConnectionClosed, "Signaling connection closed: {Reason}", reason);
        _state.Post(() =>
        {
            _state.IsServerOnline = false;
            _state.ServerStatus = global::CSIViewer.Core.Localization.Text("Sin conexión");
            _state.StatusMessage = global::CSIViewer.Core.Localization.Text("Conexión con el servidor perdida. Reconectando…");
        });

        await DisposeSignalAsync(signal).ConfigureAwait(false);

        // A reconnection must never resurrect a pending authorization or a
        // consent decision from the previous server connection.
        await FailPendingConnectAsync(
                global::CSIViewer.Core.Localization.Text("Se perdió la conexión con el servidor."),
                "server_offline")
            .ConfigureAwait(false);
        ClearPendingIncomingRequests();
        var releasedInputs = _input.ReleaseAll();
        if (releasedInputs > 0)
        {
            _logger.LogInformation(
                "Released {Released} pressed input(s) after losing the server connection",
                releasedInputs);
        }

        if (!_stopping && _state.Settings.NotificationsEnabled && !_disconnectedNotified)
        {
            _disconnectedNotified = true;
            _notifications.Show(
                global::CSIViewer.Core.Localization.Text("Conexión perdida"),
                global::CSIViewer.Core.Localization.Text("Se perdió la conexión con el servidor. Reconectando…"));
        }

        var directSession = HasSessionOrPendingStart() && _transport == TransportKind.LanDirect &&
                            _directStream is not null;
        if (directSession)
        {
            // A direct LAN session does not depend on the signaling socket.
            SetConnectionState(ConnectionState.Connected, global::CSIViewer.Core.Localization.Text("Sesión directa LAN activa; reconectando servidor…"));
        }
        else if (!_sessions.IsEmpty)
        {
            SetConnectionState(ConnectionState.Reconnecting, global::CSIViewer.Core.Localization.Text("Conexión perdida. Cerrando sesiones…"));
            var finalState = _state.Settings.AutoConnect
                ? ConnectionState.Reconnecting
                : ConnectionState.Failed;
            var directController = _sessions.Values
                .FirstOrDefault(x => x.IsController && _directStream is not null);
            foreach (var session in _sessions.Values
                         .Where(x => !ReferenceEquals(x, directController))
                         .ToArray())
            {
                await EndRemoteSessionAsync(
                        session,
                        global::CSIViewer.Core.Localization.Text("Se perdió la conexión con el servidor."),
                        sendToPeer: false,
                        finalState)
                    .ConfigureAwait(false);
            }

            if (directController is not null)
            {
                SetConnectionState(
                    ConnectionState.Connected,
                    global::CSIViewer.Core.Localization.Text("Sesión directa LAN activa; reconectando servidor…"));
            }
        }
        else if (!_stopping)
        {
            SetConnectionState(
                _state.Settings.AutoConnect ? ConnectionState.Reconnecting : ConnectionState.Failed,
                global::CSIViewer.Core.Localization.Text("Conexión con el servidor perdida."));
        }

        if (!_stopping && _state.Settings.AutoConnect)
        {
            EnsureReconnectLoop();
        }
    }

    private void EnsureReconnectLoop()
    {
        if (_stopping || !_state.Settings.AutoConnect || IsServerOnline)
        {
            return;
        }

        lock (_reconnectGate)
        {
            if (_reconnectTask is { IsCompleted: false })
            {
                return;
            }

            _reconnectCts?.Dispose();
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token));
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var policy = ReconnectPolicy.FromSettings(_state.Settings);
        var coordinator = new ReconnectCoordinator(policy);

        var outcome = await coordinator.RunAsync(
                async (attempt, token) =>
                {
                    if (_stopping || token.IsCancellationRequested)
                    {
                        return false;
                    }

                    try
                    {
                        await ConnectToServerAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _lastReconnectFailure = ex.GetBaseException().Message;
                        _logger.LogDebug(
                            ex,
                            "Reconnect attempt {Attempt}/{Max} failed",
                            attempt,
                            policy.MaxAttempts);
                    }

                    return IsServerOnline;
                },
                info => UpdateReconnectStatus(info),
                (ex, attempt) => _logger.LogDebug(
                    ex,
                    "Reconnect attempt {Attempt} threw",
                    attempt),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome == ReconnectOutcome.Exhausted)
        {
            var message = policy.MaxRetries <= 0
                ? global::CSIViewer.Core.Localization.Text("No se pudo reconectar con el servidor.")
                : global::CSIViewer.Core.Localization.Format("No se pudo reconectar tras {0} reintentos.",policy.MaxRetries);
            if (!string.IsNullOrWhiteSpace(_lastReconnectFailure) &&
                _lastReconnectFailure.Contains("HTTPS", StringComparison.OrdinalIgnoreCase))
            {
                message = _lastReconnectFailure;
            }

            _logger.LogWarning(
                LogEvents.ReconnectFailed,
                "Reconnection exhausted after {Attempts} attempts: {Message}",
                policy.MaxAttempts,
                message);

            _state.Post(() =>
            {
                _state.IsReconnecting = false;
                _state.ServerStatus = global::CSIViewer.Core.Localization.Text("Sin conexión");
                _state.StatusMessage = message;
                _state.LastError = message;
            });
            SetConnectionState(ConnectionState.Failed, message);

            if (_state.Settings.NotificationsEnabled)
            {
                _notifications.Show(global::CSIViewer.Core.Localization.Text("Sin conexión"), message);
            }
        }
        else if (outcome == ReconnectOutcome.Connected)
        {
            _lastReconnectFailure = null;
            _logger.LogInformation(
                LogEvents.ReconnectSucceeded,
                "Reconnection succeeded after {Attempts} attempts",
                policy.MaxAttempts);
            _state.Post(() =>
            {
                _state.IsReconnecting = false;
                _state.ReconnectAttempt = 0;
                _state.ReconnectMaxRetries = 0;
            });
        }
    }

    private void UpdateReconnectStatus(ReconnectAttemptInfo info)
    {
        var maxRetries = info.MaxRetries;
        var retryNumber = Math.Max(1, info.Attempt - 1);
        var seconds = Math.Max(1, (int)Math.Ceiling(info.Delay.TotalSeconds));
        if (!info.IsInitialAttempt)
        {
            SetConnectionState(ConnectionState.Reconnecting, global::CSIViewer.Core.Localization.Text("Reconectando con el servidor…"));
            _logger.LogInformation(
                LogEvents.ReconnectAttempt,
                "Reconnect attempt {Attempt}/{Max} in {DelaySeconds}s",
                retryNumber,
                maxRetries,
                seconds);
        }

        _state.Post(() =>
        {
            _state.IsReconnecting = !info.IsInitialAttempt;
            _state.ReconnectAttempt = info.IsInitialAttempt ? 0 : retryNumber;
            _state.ReconnectMaxRetries = maxRetries;
            _state.ServerStatus = info.IsInitialAttempt
                ? global::CSIViewer.Core.Localization.Text("Conectando…")
                : maxRetries > 0
                    ? $"Reconectando ({retryNumber}/{maxRetries})…"
                    : $"Reconectando (intento {retryNumber})…";
            _state.StatusMessage = info.IsInitialAttempt
                ? global::CSIViewer.Core.Localization.Text("Conectando con el servidor…")
                : global::CSIViewer.Core.Localization.Format("Reintentando en {0} s…",seconds);
        });
    }

    private async Task CancelReconnectLoopAsync()
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_reconnectGate)
        {
            task = _reconnectTask;
            _reconnectTask = null;
            cts = _reconnectCts;
            _reconnectCts = null;
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
                // The loop is already stopping.
            }
        }

        cts?.Dispose();
    }

    private void CompleteAuthentication(bool result)
    {
        lock (_connectionLock)
        {
            _authCompletion?.TrySetResult(result);
        }
    }

    private async Task DisposeSignalAsync(SignalingClient? expected = null)
    {
        SignalingClient? signal;
        lock (_connectionLock)
        {
            if (expected is not null && !ReferenceEquals(_signal, expected))
            {
                return;
            }

            signal = _signal;
            _signal = null;
            _authCompletion?.TrySetResult(false);
            _authCompletion = null;
        }

        if (signal is null)
        {
            return;
        }

        signal.EnvelopeReceived -= OnEnvelopeReceived;
        signal.BinaryReceived -= OnBinaryReceived;
        signal.ConnectionClosed -= OnConnectionClosed;
        signal.Error -= OnSignalingError;

        try
        {
            await signal.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(ShutdownTimeoutSeconds))
                .ConfigureAwait(false);
        }
        catch
        {
            // The signaling socket is already gone.
        }
    }

    private static CancellationTokenSource CreateTimeoutToken(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    private void SetConnectionState(ConnectionState state, string? statusMessage = null)
    {
        var previous = _connectionStateMachine.State;
        if (!_connectionStateMachine.TryTransition(state, out var error))
        {
            _logger.LogDebug("{TransitionError}", error);
            return;
        }

        if (previous != state)
        {
            _logger.LogInformation(
                LogEvents.ConnectionStateChanged,
                "Connection state {Previous} -> {State}",
                previous,
                state);
        }

        _state.Post(() =>
        {
            _state.ConnectionState = state;
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                _state.StatusMessage = statusMessage;
            }

            if (state is ConnectionState.Connecting or ConnectionState.Authenticating
                or ConnectionState.Connected)
            {
                _state.LastError = null;
            }
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopping = true;
        await StopHeartbeatLoopAsync().ConfigureAwait(false);
        await CancelReconnectLoopAsync().ConfigureAwait(false);

        try
        {
            await EndAllSessionsAsync(
                    global::CSIViewer.Core.Localization.Text("Aplicación cerrada."),
                    sendToPeer: false,
                    finalState: ConnectionState.Disconnected)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Session cleanup during shutdown failed");
        }

        await DisposeSignalAsync().ConfigureAwait(false);

        _lifetimeCts.Cancel();
        _receiveQueue.Writer.TryComplete();
        if (_messageLoopTask is not null)
        {
            try
            {
                await _messageLoopTask.WaitAsync(TimeSpan.FromSeconds(ShutdownTimeoutSeconds))
                    .ConfigureAwait(false);
            }
            catch
            {
                // The message loop is already stopping.
            }
        }

        try
        {
            await StopDisplayWatcherAsync().ConfigureAwait(false);
            await _modernCapture.DisposeAsync().ConfigureAwait(false);
            await _capture.DisposeAsync().ConfigureAwait(false);
            _h264Encoder?.Dispose();
            _h264Encoder = null;
            _h264Decoder?.Dispose();
            _h264Decoder = null;
        }
        catch
        {
            // Capture shutdown is best effort.
        }

        lock (_gate)
        {
            _crypto?.Dispose();
            _crypto = null;
            _handshake?.Dispose();
            _handshake = null;
        }

        // These synchronization primitives live with the client instance. They
        // are intentionally not disposed while the process is shutting down:
        // a UI-initiated connect/send may still be unwinding.
        _lifetimeCts.Dispose();
    }
}

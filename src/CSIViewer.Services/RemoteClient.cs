using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Media;
using CSIViewer.Networking;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.Services;

public sealed partial class RemoteClient : IAsyncDisposable
{
    private readonly AppState _state;
    private readonly IdentityService _identity;
    private readonly ISettingsStore _settingsStore;
    private readonly IKnownDeviceRepository _knownDevices;
    private readonly IDeviceListRepository _deviceLists;
    private readonly IHistoryRepository _history;
    private readonly IChatRepository _chatRepository;
    private readonly ITransferRepository _transferRepository;
    private readonly IContactRepository _contacts;
    private readonly IConsentService _consent;
    private readonly INotificationService _notifications;
    private readonly ILogger<RemoteClient> _logger;
    private readonly GdiScreenCapture _capture = new();
    private readonly WindowsGraphicsCapture _modernCapture = new();
    private readonly InputInjector _input = new();
    private H264Encoder? _h264Encoder;
    private H264Decoder? _h264Decoder;
    private int _h264Width;
    private int _h264Height;
    private int _h264Fps;
    private bool _h264EncoderDisabled;
    private readonly SemaphoreSlim _directSendLock = new(1, 1);
    private readonly object _directGate = new();
    private int _videoSendBusy;
    private long _droppedVideoFrames;
    private DirectSessionServer? _directServer;
    private NetworkStream? _directStream;
    private CancellationTokenSource? _directCts;
    private Task? _directReceiveTask;
    private TransportKind _transport = TransportKind.Relay;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, string> _knownDeviceKeys =
        new(StringComparer.Ordinal);

    private SignalingClient? _signal;
    private SessionHandshake? _handshake;
    private SessionCrypto? _crypto;
    private string? _activeSessionId;
    private string? _remoteId;
    private string? _remoteName;
    private bool _isController;
    private SessionPermissions _permissions;
    private DateTimeOffset? _sessionStarted;
    private string? _remotePublicKey;
    private PendingConnect? _pendingConnect;

    public RemoteClient(
        AppState state,
        IdentityService identity,
        ISettingsStore settingsStore,
        IKnownDeviceRepository knownDevices,
        IDeviceListRepository deviceLists,
        IHistoryRepository history,
        IChatRepository chatRepository,
        ITransferRepository transferRepository,
        IContactRepository contacts,
        IConsentService consent,
        INotificationService notifications,
        ILogger<RemoteClient> logger,
        ChatHistoryOptions? chatHistoryOptions = null)
    {
        _state = state;
        _identity = identity;
        _settingsStore = settingsStore;
        _knownDevices = knownDevices;
        _deviceLists = deviceLists;
        _history = history;
        _chatRepository = chatRepository;
        _retainChatHistory = chatHistoryOptions?.RetainHistory ?? true;
        _transferRepository = transferRepository;
        _contacts = contacts;
        _consent = consent;
        _notifications = notifications;
        _logger = logger;
        _incomingFiles = new();
        _outgoingFiles = new();
        StartBackgroundProcessing();
    }

    public event EventHandler<VideoFrame>? VideoFrameReceived;
    public event EventHandler<MonitorsPayload>? MonitorsReceived;
    public event EventHandler<ChatMessage>? ChatMessageReceived;
    public event EventHandler<TransferItem>? TransferUpdated;
    public event EventHandler<SessionInfo>? SessionStarted;
    public event EventHandler<string>? SessionEnded;

    public bool IsServerOnline => _signal?.IsConnected == true && _state.IsServerOnline;
    public bool IsSessionActive => _state.IsSessionActive;
    public ControlRestriction RemoteControlRestriction => GetPrimarySession()?.ControlRestriction ?? ControlRestriction.None;
    public long DroppedVideoFrames => Interlocked.Read(ref _droppedVideoFrames);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _identity.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!_retainChatHistory)
        {
            // Remove legacy host transcripts once on startup; never read them into the UI.
            foreach (var peer in await _chatRepository.GetPeerDeviceIdsAsync(cancellationToken).ConfigureAwait(false))
                await _chatRepository.DeleteThreadAsync(peer, cancellationToken).ConfigureAwait(false);
        }
        await LoadLocalDataAsync(cancellationToken).ConfigureAwait(false);
        UpdateSessionManagerLimit();
        StartHeartbeatLoop();

        if (_state.Settings.AutoConnect)
        {
            EnsureReconnectLoop();
        }
    }

    private async Task LoadLocalDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var devices = await _knownDevices.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var deviceLists = await _deviceLists.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var history = await _history.GetRecentAsync(200, cancellationToken).ConfigureAwait(false);
            var transfers = await _transferRepository.GetRecentAsync(100, cancellationToken).ConfigureAwait(false);
            var contacts = await _contacts.GetAllAsync(cancellationToken).ConfigureAwait(false);

            _knownDeviceKeys.Clear();
            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.PublicKey))
                {
                    _knownDeviceKeys[device.DeviceId] = device.PublicKey;
                }
            }

            _state.Post(() =>
            {
                _state.KnownDevices.Clear();
                foreach (var device in devices)
                {
                    _state.KnownDevices.Add(device);
                }

                _state.DeviceLists.Clear();
                foreach (var list in deviceLists)
                {
                    _state.DeviceLists.Add(list);
                }

                _state.History.Clear();
                foreach (var entry in history)
                {
                    _state.History.Add(entry);
                }

                _state.Transfers.Clear();
                ResetLoadedTransfers();
                foreach (var transfer in transfers)
                {
                    // A process that was killed mid-transfer cannot still be
                    // transferring on startup. Keep the item resumable instead
                    // of showing a permanently active state.
                    if (transfer.State is TransferState.Queued or TransferState.Preparing
                        or TransferState.Transferring or TransferState.Verifying)
                    {
                        transfer.State = TransferState.Interrupted;
                        transfer.BytesPerSecond = 0;
                        transfer.Error = "Transferencia interrumpida. Se puede reanudar.";
                    }

                    TrackLoadedTransfer(transfer);
                    _state.Transfers.Add(transfer);
                }

                _state.Contacts.Clear();
                foreach (var contact in contacts)
                {
                    _state.Contacts.Add(contact);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load local data");
        }
    }

    private async Task HandleEnvelopeAsync(Envelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageTypes.AuthChallenge:
            {
                var challenge = ProtocolJson.Payload<AuthChallenge>(envelope);
                if (challenge is null || _state.Identity is null || _signal is null)
                {
                    return;
                }

                var signed = Encoding.UTF8.GetBytes($"RD-AUTH-v1|{challenge.Nonce}|{_state.DeviceId}");
                var signature = Convert.ToBase64String(_state.Identity.Sign(signed));
                using var sendTimeout = CreateTimeoutToken(
                    TimeSpan.FromSeconds(10),
                    _lifetimeCts.Token);
                await _signal.SendAsync(
                        ProtocolJson.Create(
                            MessageTypes.AuthRequest,
                            new AuthRequest(_state.DeviceId, signature)),
                        sendTimeout.Token)
                    .ConfigureAwait(false);
                break;
            }

            case MessageTypes.AuthOk:
            {
                var auth = ProtocolJson.Payload<AuthOk>(envelope);
                if (auth is not null)
                {
                    _state.Post(() =>
                    {
                        _state.DeviceId = auth.DeviceId;
                        _state.IsServerOnline = true;
                        _state.ServerStatus = global::CSIViewer.Core.Localization.Text("En línea");
                        _state.StatusMessage = global::CSIViewer.Core.Localization.Text("Conectado. Disponible para recibir asistencia.");
                    });
                    CompleteAuthentication(true);
                }

                break;
            }

            case MessageTypes.AuthError:
            {
                var error = ProtocolJson.Payload<AuthError>(envelope);
                _state.Post(() =>
                {
                    _state.IsServerOnline = false;
                    _state.ServerStatus = global::CSIViewer.Core.Localization.Text("No autorizado");
                    _state.LastError = error?.Message ?? global::CSIViewer.Core.Localization.Text("Autenticación rechazada.");
                });
                CompleteAuthentication(false);
                break;
            }

            case MessageTypes.Presence:
            {
                var presence = ProtocolJson.Payload<PresenceMessage>(envelope);
                if (presence is not null)
                {
                    UpdatePresence(presence);
                }

                break;
            }

            case MessageTypes.ConnectIncoming:
                await HandleIncomingConnectAsync(envelope).ConfigureAwait(false);
                break;

            case MessageTypes.ConnectPending:
                await HandleConnectPendingAsync(envelope).ConfigureAwait(false);
                break;

            case MessageTypes.ConnectResult:
                await HandleConnectResultAsync(envelope).ConfigureAwait(false);
                break;

            case MessageTypes.ConnectError:
            {
                var error = ProtocolJson.Payload<ConnectErrorPayload>(envelope);
                var message = error is not null &&
                              !string.IsNullOrWhiteSpace(error.Message) &&
                              MessageValidation.IsValidReason(error.Message)
                    ? error.Message
                    : global::CSIViewer.Core.Localization.Text("No se pudo iniciar la conexión.");
                await FailPendingConnectAsync(
                        message,
                        error?.Code ?? "connect_failed")
                    .ConfigureAwait(false);
                break;
            }

            case MessageTypes.SessionEnded:
            {
                var payload = ProtocolJson.Payload<SessionEndPayload>(envelope);
                if (payload is not null && Guid.TryParse(payload.SessionId, out var endedSessionId))
                {
                    await EndSessionByIdAsync(
                            endedSessionId,
                            payload.Reason,
                            sendToPeer: false,
                            finalState: _state.IsServerOnline
                                ? ConnectionState.Connected
                                : ConnectionState.Disconnected)
                        .ConfigureAwait(false);
                }

                break;
            }

            case MessageTypes.Error:
            {
                var payload = ProtocolJson.Payload<ErrorPayload>(envelope);
                var safeMessage = payload is not null &&
                                  !string.IsNullOrWhiteSpace(payload.Message) &&
                                  MessageValidation.IsValidReason(payload.Message)
                    ? payload.Message
                    : global::CSIViewer.Core.Localization.Text("Error del servidor.");
                _logger.LogWarning("Server error: {Code} {Message}", payload?.Code, safeMessage);
                _state.Post(() => _state.LastError = safeMessage);

                // Admission failures must complete the pending connection
                // immediately instead of letting it wait for the 65 s timeout.
                if (payload is not null &&
                    payload.Code is "host_full" or "controller_busy" or "host_offline"
                        or "host_unreachable" or "unknown_device" or "self_connect"
                        or "session_exists" or "invalid_connect" or "rate_limited")
                {
                    await FailPendingConnectAsync(
                            payload.Message,
                            payload.Code)
                        .ConfigureAwait(false);
                }

                break;
            }
        }
    }

    private void UpdatePresence(PresenceMessage presence)
    {
        _state.Post(() =>
        {
            for (var i = 0; i < _state.KnownDevices.Count; i++)
            {
                var device = _state.KnownDevices[i];
                if (device.DeviceId == presence.DeviceId)
                {
                    _state.KnownDevices[i] = device with
                    {
                        IsOnline = presence.Online,
                        LastSeenUtc = presence.LastSeenUtc ?? device.LastSeenUtc
                    };
                }
            }
        });
    }

    private sealed class PendingConnect
    {
        public required string SessionId { get; init; }
        public required string TargetId { get; init; }
        public required string Password { get; init; }
        public required SessionPermissions Permissions { get; init; }
        public required bool ChatOnly { get; init; }
        public required SessionHandshake Handshake { get; init; }
        public required byte[] ClientTranscript { get; init; }
        public TaskCompletionSource<Result<bool>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class IncomingPending
    {
        public required string SessionId { get; init; }
        public required string FromDeviceId { get; init; }
        public required string FromName { get; init; }
        public required SessionPermissions Permissions { get; init; }
        public required string ControllerPublicKey { get; init; }
        public required string ControllerEphemeralPublicKey { get; init; }
        public required string ControllerNonce { get; init; }
        public required bool ChatOnly { get; init; }
        public required DateTimeOffset ExpiresUtc { get; init; }
        public required SessionHandshake Handshake { get; init; }
        public required byte[] FinalTranscript { get; init; }
        public required SessionCrypto Crypto { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
    }
}

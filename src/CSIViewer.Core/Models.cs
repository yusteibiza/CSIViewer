namespace CSIViewer.Core;

[Flags]
public enum SessionPermissions
{
    None = 0,
    ViewScreen = 1,
    ControlInput = 2,
    Chat = 4,
    SendFiles = 8,
    ReceiveFiles = 16,
    Audio = 32,
    Clipboard = 64,
    All = ViewScreen | ControlInput | Chat | SendFiles | ReceiveFiles | Audio | Clipboard
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Disconnecting,
    Failed
}

public enum ConnectionDirection
{
    Outgoing,
    Incoming
}

public enum ConnectionResult
{
    Success,
    Rejected,
    Failed,
    Cancelled,
    Timeout
}

public enum TransportKind
{
    Unknown,
    LanDirect,
    Relay
}

public sealed record DeviceInfo(
    string DeviceId,
    string Name,
    string PublicKey,
    bool IsOnline = false,
    DateTimeOffset? LastSeenUtc = null);

public sealed record KnownDevice(
    string DeviceId,
    string Alias,
    string Description,
    string GroupName,
    bool IsFavorite,
    bool IsOnline,
    DateTimeOffset LastSeenUtc,
    string PublicKey = "",
    string? ListId = null,
    string? ProtectedPassword = null);

public sealed record DeviceList(
    string Id,
    string Name,
    DateTimeOffset CreatedUtc);

public sealed record Contact(
    string Id,
    string DisplayName,
    string Notes,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<string>? DeviceIds = null);

public sealed record ConnectionHistoryEntry(
    string Id,
    string RemoteDeviceId,
    string RemoteName,
    ConnectionDirection Direction,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    TimeSpan Duration,
    TransportKind Transport,
    ConnectionResult Result,
    string DisconnectReason,
    string? ErrorCode);

public enum ChatDeliveryState
{
    Sending,
    Sent,
    Received,
    Failed
}

public sealed record ChatMessage(
    string Id,
    string PeerDeviceId,
    string SenderDeviceId,
    string Text,
    DateTimeOffset SentUtc,
    bool IsMine,
    ChatDeliveryState DeliveryState = ChatDeliveryState.Sent);

public enum TransferDirection
{
    Send,
    Receive
}

public enum TransferState
{
    Queued,
    Preparing,
    Transferring,
    Paused,
    Verifying,
    Completed,
    Cancelled,
    Failed,
    Interrupted
}

public sealed class TransferItem
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required TransferDirection Direction { get; init; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public TransferState State { get; set; } = TransferState.Queued;
    public double BytesPerSecond { get; set; }
    public string? Error { get; set; }
    public string? LocalPath { get; set; }
    public string? BrowserDestination { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public double Progress => TotalBytes <= 0 ? 0 : Math.Clamp((double)TransferredBytes / TotalBytes, 0, 1);
    public string SpeedText => BytesPerSecond <= 0 ? "" : $"{BytesPerSecond / 1024 / 1024:0.0} MB/s";
    public string DirectionText => Direction == TransferDirection.Send ? global::CSIViewer.Core.Localization.Text("Envío") : global::CSIViewer.Core.Localization.Text("Recepción");
    public bool IsActive => State is TransferState.Queued or TransferState.Preparing
        or TransferState.Transferring or TransferState.Paused or TransferState.Verifying;
    public bool CanPause => State == TransferState.Transferring;
    public bool CanResume => State is TransferState.Paused or TransferState.Interrupted;
    public bool CanCancel => IsActive || State == TransferState.Interrupted;
    public bool CanRemove => !IsActive;
    public string StateText => State switch
    {
        TransferState.Queued => global::CSIViewer.Core.Localization.Text("En cola"),
        TransferState.Preparing => global::CSIViewer.Core.Localization.Text("Preparando"),
        TransferState.Transferring => global::CSIViewer.Core.Localization.Text("Transfiriendo"),
        TransferState.Paused => global::CSIViewer.Core.Localization.Text("Pausada"),
        TransferState.Verifying => global::CSIViewer.Core.Localization.Text("Verificando SHA-256"),
        TransferState.Completed => global::CSIViewer.Core.Localization.Text("Completada"),
        TransferState.Cancelled => global::CSIViewer.Core.Localization.Text("Cancelada"),
        TransferState.Failed => global::CSIViewer.Core.Localization.Text("Error"),
        TransferState.Interrupted => global::CSIViewer.Core.Localization.Text("Interrumpida"),
        _ => State.ToString()
    };
    public string EtaText
    {
        get
        {
            if (BytesPerSecond <= 0 || TotalBytes <= TransferredBytes)
            {
                return "";
            }

            var seconds = (TotalBytes - TransferredBytes) / BytesPerSecond;
            return seconds < 60
                ? $"~{seconds:0} s restantes"
                : $"~{seconds / 60:0} min restantes";
        }
    }
}

public sealed record IncomingConnectionRequest(
    string SessionId,
    string FromDeviceId,
    string FromName,
    SessionPermissions RequestedPermissions,
    string HostPublicKey,
    string ControllerPublicKey,
    string ControllerEphemeralPublicKey,
    string ControllerNonce,
    string ControllerSignature,
    byte[] SharedSecret,
    byte[] Transcript,
    DateTimeOffset ReceivedUtc);

public sealed record SessionInfo(
    string SessionId,
    string RemoteDeviceId,
    string RemoteName,
    SessionPermissions Permissions,
    bool IsController,
    TransportKind Transport,
    DateTimeOffset StartedUtc);

public sealed record AppError(string Code, string Message, Exception? Exception = null);

public readonly record struct Result<T>(bool IsSuccess, T? Value, AppError? Error)
{
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string code, string message) => new(false, default, new AppError(code, message));
}

public static class PermissionNames
{
    public static string Describe(SessionPermissions permissions)
    {
        if (permissions == SessionPermissions.None)
        {
            return global::CSIViewer.Core.Localization.Text("Ninguno");
        }

        var names = new List<string>();
        if (permissions.HasFlag(SessionPermissions.ViewScreen)) names.Add(global::CSIViewer.Core.Localization.Text("Ver pantalla"));
        if (permissions.HasFlag(SessionPermissions.ControlInput)) names.Add("Controlar entrada");
        if (permissions.HasFlag(SessionPermissions.Chat)) names.Add(global::CSIViewer.Core.Localization.Text("Chat"));
        if (permissions.HasFlag(SessionPermissions.SendFiles)) names.Add("Enviar archivos");
        if (permissions.HasFlag(SessionPermissions.ReceiveFiles)) names.Add("Recibir archivos");
        if (permissions.HasFlag(SessionPermissions.Audio)) names.Add(Localization.Text("Audio del sistema"));
        if (permissions.HasFlag(SessionPermissions.Clipboard)) names.Add(Localization.Text("Portapapeles de texto"));
        return string.Join(", ", names);
    }
}

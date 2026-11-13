using CSIViewer.Core;

namespace CSIViewer.Protocol;

public static class MessageTypes
{
    public const string AuthChallenge = "auth.challenge";
    public const string AuthRequest = "auth.request";
    public const string AuthOk = "auth.ok";
    public const string AuthError = "auth.error";
    public const string Presence = "presence";
    public const string PresenceSet = "presence.set";
    public const string ConnectRequest = "connect.request";
    public const string ConnectIncoming = "connect.incoming";
    public const string ConnectPending = "connect.pending";
    public const string ConnectResponse = "connect.response";
    public const string ConnectResult = "connect.result";
    public const string ConnectError = "connect.error";
    public const string SessionEnd = "session.end";
    public const string SessionEnded = "session.ended";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
}

public sealed record RegisterDeviceRequest(string Name, string PublicKey, string EnrollmentCode);
public sealed record RegisterDeviceResponse(string DeviceId, string Name);

public sealed record AuthChallenge(string Nonce, long ServerTimeUnixMs);
public sealed record AuthRequest(string DeviceId, string Signature);
public sealed record AuthOk(string DeviceId, string Name);
public sealed record AuthError(string Code, string Message);

public sealed record PresenceMessage(string DeviceId, bool Online, string Name, DateTimeOffset? LastSeenUtc = null);

public sealed record ConnectRequestPayload(
    string SessionId,
    string TargetId,
    SessionPermissions Permissions,
    string EphemeralPublicKey,
    string Nonce,
    string Signature,
    string ControllerPublicKey,
    bool ChatOnly);

public sealed record ConnectIncomingPayload(
    string SessionId,
    string FromDeviceId,
    string FromName,
    SessionPermissions Permissions,
    string EphemeralPublicKey,
    string Nonce,
    string Signature,
    string ControllerPublicKey,
    bool ChatOnly,
    DateTimeOffset ExpiresUtc);

public sealed record ConnectPendingPayload(
    string SessionId,
    string EphemeralPublicKey,
    string Nonce,
    string Signature,
    string HostPublicKey,
    string HostName,
    string? DirectEndPoint = null,
    string? DirectToken = null);

public sealed record ConnectResponsePayload(
    string SessionId,
    bool Accepted,
    SessionPermissions GrantedPermissions,
    string? Reason = null);

public sealed record ConnectResultPayload(
    string SessionId,
    bool Accepted,
    SessionPermissions GrantedPermissions,
    string RemoteDeviceId,
    string RemoteName,
    string? Reason = null);

public sealed record ConnectErrorPayload(string Code, string Message, string? TargetId = null);

public sealed record SessionEndPayload(string SessionId, string Reason);

public sealed record PingPayload(long TimestampUnixMs);
public sealed record PongPayload(long TimestampUnixMs);

public sealed record ErrorPayload(string Code, string Message);

using CSIViewer.Core;

namespace CSIViewer.Application;

public sealed record StoredIdentity(
    string? DeviceId,
    string DeviceName,
    byte[] PrivateKeyPkcs8,
    string PublicKeyBase64);

public interface IIdentityStore
{
    Task<StoredIdentity?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredIdentity identity, CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ICustomPasswordStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string? password, CancellationToken cancellationToken = default);
}

public interface IKnownDeviceRepository
{
    Task<IReadOnlyList<KnownDevice>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(KnownDevice device, CancellationToken cancellationToken = default);
    // Automatic connection updates must never replace user-edited details or credentials.
    Task<KnownDevice> RecordConnectionAsync(string deviceId, string remoteName, DateTimeOffset seenUtc,
        string publicKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string deviceId, CancellationToken cancellationToken = default);
}

public interface IDeviceListRepository
{
    Task<IReadOnlyList<DeviceList>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(DeviceList list, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IHistoryRepository
{
    Task<IReadOnlyList<ConnectionHistoryEntry>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default);
    Task AddAsync(ConnectionHistoryEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}

public interface IChatRepository
{
    Task<IReadOnlyList<ChatMessage>> GetThreadAsync(string peerDeviceId, int limit = 500, CancellationToken cancellationToken = default);
    Task AddAsync(ChatMessage message, CancellationToken cancellationToken = default);
}

public interface ITransferRepository
{
    Task<IReadOnlyList<TransferItem>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default);
    Task UpsertAsync(TransferItem item, CancellationToken cancellationToken = default);
}

public interface IContactRepository
{
    Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Contact contact, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record ConsentRequest(
    string SessionId,
    string FromDeviceId,
    string FromName,
    SessionPermissions RequestedPermissions,
    bool ChatOnly,
    string Fingerprint);

public sealed record ConsentDecision(bool Accepted, SessionPermissions GrantedPermissions);

public enum FileReceiveDecision
{
    Reject,
    Accept,
    AcceptRename
}

public interface IConsentService
{
    Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default);

    Task<FileReceiveDecision> RequestFileReceiveAsync(
        string fromName,
        string fileName,
        long size,
        bool destinationExists,
        bool willOverwrite,
        CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    void Show(string title, string message);
}

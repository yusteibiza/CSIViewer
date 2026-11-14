using Microsoft.EntityFrameworkCore;
using CSIViewer.Application;
using CSIViewer.Core;

namespace CSIViewer.Infrastructure.Persistence;

public sealed class KnownDeviceRepository : IKnownDeviceRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public KnownDeviceRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<KnownDevice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var devices = await db.KnownDevices.AsNoTracking()
            .Select(x => new KnownDevice(
                x.DeviceId, x.Alias, x.Description, x.GroupName, x.IsFavorite,
                false, x.LastSeenUtc, x.PublicKey, x.ListId, x.ProtectedPassword))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return devices.OrderByDescending(x => x.LastSeenUtc).ToList();
    }

    public async Task UpsertAsync(KnownDevice device, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.KnownDevices.FindAsync([device.DeviceId], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new KnownDeviceEntity { DeviceId = device.DeviceId };
            db.KnownDevices.Add(entity);
        }

        entity.Alias = string.IsNullOrWhiteSpace(device.Alias) ? device.DeviceId : device.Alias;
        entity.Description = device.Description;
        entity.GroupName = device.GroupName;
        entity.IsFavorite = device.IsFavorite;
        entity.LastSeenUtc = device.LastSeenUtc;
        entity.PublicKey = device.PublicKey;
        entity.ListId = device.ListId;
        entity.ProtectedPassword = device.ProtectedPassword;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.KnownDevices.FindAsync([deviceId], cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            db.KnownDevices.Remove(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<KnownDevice> RecordConnectionAsync(string deviceId, string remoteName,
        DateTimeOffset seenUtc, string publicKey, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.KnownDevices.FindAsync([deviceId], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new KnownDeviceEntity
            {
                DeviceId = deviceId,
                Alias = string.IsNullOrWhiteSpace(remoteName) ? deviceId : remoteName
            };
            db.KnownDevices.Add(entity);
        }

        // EF updates only modified connection fields. In particular, it must not
        // write a stale/null password from the UI cache over the database value.
        if (seenUtc > entity.LastSeenUtc) entity.LastSeenUtc = seenUtc;
        if (!string.IsNullOrWhiteSpace(publicKey)) entity.PublicKey = publicKey;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new KnownDevice(entity.DeviceId, entity.Alias, entity.Description, entity.GroupName,
            entity.IsFavorite, true, entity.LastSeenUtc, entity.PublicKey, entity.ListId, entity.ProtectedPassword);
    }
}

public sealed class DeviceListRepository : IDeviceListRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public DeviceListRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<DeviceList>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var lists = await db.DeviceLists.AsNoTracking()
            .Select(x => new DeviceList(x.Id, x.Name, x.CreatedUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return lists.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task UpsertAsync(DeviceList list, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.DeviceLists.FindAsync([list.Id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new DeviceListEntity { Id = list.Id, CreatedUtc = list.CreatedUtc };
            db.DeviceLists.Add(entity);
        }

        entity.Name = list.Name;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.DeviceLists.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        // Devices stay saved; they simply stop belonging to a custom list.
        var devices = await db.KnownDevices
            .Where(x => x.ListId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var device in devices)
        {
            device.ListId = null;
            device.GroupName = "";
        }

        db.DeviceLists.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public HistoryRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<ConnectionHistoryEntry>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entries = await db.History.AsNoTracking()
            .Select(x => new ConnectionHistoryEntry(
                x.Id, x.RemoteDeviceId, x.RemoteName,
                (ConnectionDirection)x.Direction,
                x.StartedUtc, x.EndedUtc,
                TimeSpan.FromSeconds(x.DurationSeconds),
                (TransportKind)x.Transport,
                (ConnectionResult)x.Result,
                x.DisconnectReason, x.ErrorCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entries.OrderByDescending(x => x.StartedUtc).Take(limit).ToList();
    }

    public async Task AddAsync(ConnectionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.History.Add(new HistoryEntity
        {
            Id = entry.Id,
            RemoteDeviceId = entry.RemoteDeviceId,
            RemoteName = entry.RemoteName,
            Direction = (int)entry.Direction,
            StartedUtc = entry.StartedUtc,
            EndedUtc = entry.EndedUtc,
            DurationSeconds = (long)entry.Duration.TotalSeconds,
            Transport = (int)entry.Transport,
            Result = (int)entry.Result,
            DisconnectReason = entry.DisconnectReason,
            ErrorCode = entry.ErrorCode
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.History.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ChatRepository : IChatRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public ChatRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<string>> GetPeerDeviceIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ChatMessages.AsNoTracking().Where(x => x.PeerDeviceId != "")
            .Select(x => x.PeerDeviceId).Distinct().OrderBy(x => x)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteThreadAsync(string peerDeviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerDeviceId);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.ChatMessages.Where(x => x.PeerDeviceId == peerDeviceId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetThreadAsync(string peerDeviceId, int limit = 500, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(x => x.PeerDeviceId == peerDeviceId)
            .Select(x => new ChatMessage(
                x.Id, x.PeerDeviceId, x.SenderDeviceId, x.Text, x.SentUtc,
                x.IsMine, (ChatDeliveryState)x.DeliveryState))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return messages.OrderByDescending(x => x.SentUtc).Take(limit).OrderBy(x => x.SentUtc).ToList();
    }

    public async Task AddAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.ChatMessages.AnyAsync(x => x.Id == message.Id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        db.ChatMessages.Add(new ChatMessageEntity
        {
            Id = message.Id,
            PeerDeviceId = message.PeerDeviceId,
            SenderDeviceId = message.SenderDeviceId,
            Text = message.Text,
            SentUtc = message.SentUtc,
            IsMine = message.IsMine,
            DeliveryState = (int)message.DeliveryState
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ContactRepository : IContactRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public ContactRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Contact>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var contacts = await db.Contacts.AsNoTracking()
            .OrderByDescending(x => x.IsFavorite).ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return contacts.Select(x => new Contact(
                x.Id,
                x.DisplayName,
                x.Notes,
                x.CreatedUtc,
                x.DeviceIds.Split(';', StringSplitOptions.RemoveEmptyEntries),
                x.Company, x.Email, x.Phone, x.IsFavorite))
            .ToList();
    }

    public async Task UpsertAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Contacts.FindAsync([contact.Id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new ContactEntity { Id = contact.Id, CreatedUtc = contact.CreatedUtc };
            db.Contacts.Add(entity);
        }

        entity.DisplayName = contact.DisplayName;
        entity.Notes = contact.Notes;
        entity.DeviceIds = string.Join(';', contact.DeviceIds ?? []);
        entity.Company = contact.Company;
        entity.Email = contact.Email;
        entity.Phone = contact.Phone;
        entity.IsFavorite = contact.IsFavorite;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Contacts.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (entity is not null)
        {
            db.Contacts.Remove(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class TransferRepository : ITransferRepository
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public TransferRepository(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<TransferItem>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var transfers = await db.Transfers.AsNoTracking()
            .Select(x => new TransferItem
            {
                Id = x.Id,
                FileName = x.FileName,
                Direction = (TransferDirection)x.Direction,
                TotalBytes = x.TotalBytes,
                TransferredBytes = x.TransferredBytes,
                State = (TransferState)x.State,
                BytesPerSecond = x.BytesPerSecond,
                Error = x.Error,
                LocalPath = x.LocalPath,
                BrowserDestination = x.BrowserDestination,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return transfers.OrderByDescending(x => x.UpdatedUtc).Take(limit).ToList();
    }

    public async Task UpsertAsync(TransferItem item, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Transfers.FindAsync([item.Id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new TransferEntity { Id = item.Id };
            db.Transfers.Add(entity);
        }

        entity.FileName = item.FileName;
        entity.Direction = (int)item.Direction;
        entity.TotalBytes = item.TotalBytes;
        entity.TransferredBytes = item.TransferredBytes;
        entity.State = (int)item.State;
        entity.BytesPerSecond = item.BytesPerSecond;
        entity.Error = item.Error;
        entity.LocalPath = item.LocalPath;
        entity.BrowserDestination = item.BrowserDestination;
        entity.UpdatedUtc = item.UpdatedUtc;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

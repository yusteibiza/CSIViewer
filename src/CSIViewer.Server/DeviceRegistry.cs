using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using CSIViewer.Server.Persistence;

namespace CSIViewer.Server;

public sealed class ServerOptions
{
    public string EnrollmentCode { get; set; } = global::CSIViewer.Core.Localization.Text("DEV-1234");
    public string ServerName { get; set; } = global::CSIViewer.Core.Localization.Text("CSIViewer Servidor");
    public int MaxSessionsPerHost { get; set; } = 10;
}

public sealed class DeviceRegistry
{
    private readonly IDbContextFactory<ServerDbContext> _factory;

    public DeviceRegistry(IDbContextFactory<ServerDbContext> factory) => _factory = factory;

    public async Task<ServerDevice?> GetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deviceId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ServerDevice> RegisterAsync(
        string name,
        string publicKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new ArgumentException(global::CSIViewer.Core.Localization.Text("La clave pública del dispositivo no es válida."), nameof(publicKey), ex);
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string id;
        do
        {
            id = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000)
                .ToString(CultureInfo.InvariantCulture);
        }
        while (await db.Devices.AnyAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false));

        var device = new ServerDevice
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? global::CSIViewer.Core.Localization.Text("Equipo Windows") : name.Trim(),
            PublicKey = publicKey,
            CreatedUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }

    public async Task<IReadOnlyList<ServerDevice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Devices.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var device = await db.Devices.FindAsync([deviceId], cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return false;
        }

        device.Revoked = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> RevokeAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var devices = await db.Devices.Where(x => !x.Revoked).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var device in devices)
        {
            device.Revoked = true;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return devices.Count;
    }

    public async Task TouchAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var device = await db.Devices.FindAsync([deviceId], cancellationToken).ConfigureAwait(false);
        if (device is not null)
        {
            device.LastSeenUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RecordSessionStartAsync(
        string sessionId,
        string controllerId,
        string hostId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.Sessions.FindAsync([sessionId], cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        db.Sessions.Add(new ServerSession
        {
            Id = sessionId,
            ControllerId = controllerId,
            HostId = hostId,
            StartedUtc = DateTimeOffset.UtcNow,
            Result = "pending"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSessionEndAsync(
        string sessionId,
        string result,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await db.Sessions.FindAsync([sessionId], cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return;
        }

        session.EndedUtc = DateTimeOffset.UtcNow;
        session.Result = result;
        session.Reason = reason;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AuditAsync(
        string category,
        string? deviceId,
        string? peerDeviceId,
        string details,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Audit.Add(new ServerAudit
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Category = category,
            DeviceId = deviceId,
            PeerDeviceId = peerDeviceId,
            Details = details
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

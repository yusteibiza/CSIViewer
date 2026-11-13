using Microsoft.EntityFrameworkCore;

namespace CSIViewer.Server.Persistence;

public sealed class ServerDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public bool Revoked { get; set; }
}

public sealed class ServerSession
{
    public string Id { get; set; } = "";
    public string ControllerId { get; set; } = "";
    public string HostId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public string Result { get; set; } = "pending";
    public string Reason { get; set; } = "";
}

public sealed class ServerAudit
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string Category { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? PeerDeviceId { get; set; }
    public string Details { get; set; } = "";
}

public sealed class ServerDbContext : DbContext
{
    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options)
    {
    }

    public DbSet<ServerDevice> Devices => Set<ServerDevice>();
    public DbSet<ServerSession> Sessions => Set<ServerSession>();
    public DbSet<ServerAudit> Audit => Set<ServerAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerDevice>().HasKey(x => x.Id);
        modelBuilder.Entity<ServerDevice>().HasIndex(x => x.LastSeenUtc);
        modelBuilder.Entity<ServerSession>().HasKey(x => x.Id);
        modelBuilder.Entity<ServerSession>().HasIndex(x => x.HostId);
        modelBuilder.Entity<ServerAudit>().HasKey(x => x.Id);
    }
}

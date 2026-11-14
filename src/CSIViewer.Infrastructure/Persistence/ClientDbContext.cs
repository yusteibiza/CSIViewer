using Microsoft.EntityFrameworkCore;
using CSIViewer.Core;

namespace CSIViewer.Infrastructure.Persistence;

public sealed class KnownDeviceEntity
{
    public string DeviceId { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Description { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool IsFavorite { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public string PublicKey { get; set; } = "";
    public string? ListId { get; set; }
    public string? ProtectedPassword { get; set; }
}

public sealed class DeviceListEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class HistoryEntity
{
    public string Id { get; set; } = "";
    public string RemoteDeviceId { get; set; } = "";
    public string RemoteName { get; set; } = "";
    public int Direction { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public long DurationSeconds { get; set; }
    public int Transport { get; set; }
    public int Result { get; set; }
    public string DisconnectReason { get; set; } = "";
    public string? ErrorCode { get; set; }
}

public sealed class ChatMessageEntity
{
    public string Id { get; set; } = "";
    public string PeerDeviceId { get; set; } = "";
    public string SenderDeviceId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset SentUtc { get; set; }
    public bool IsMine { get; set; }
    public int DeliveryState { get; set; }
}

public sealed class ContactEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public string DeviceIds { get; set; } = "";
    public string Company { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsFavorite { get; set; }
}

public sealed class TransferEntity
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public int Direction { get; set; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public int State { get; set; }
    public double BytesPerSecond { get; set; }
    public string? Error { get; set; }
    public string? LocalPath { get; set; }
    public string? BrowserDestination { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ClientDbContext : DbContext
{
    public ClientDbContext(DbContextOptions<ClientDbContext> options) : base(options)
    {
    }

    public DbSet<KnownDeviceEntity> KnownDevices => Set<KnownDeviceEntity>();
    public DbSet<DeviceListEntity> DeviceLists => Set<DeviceListEntity>();
    public DbSet<HistoryEntity> History => Set<HistoryEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<ContactEntity> Contacts => Set<ContactEntity>();
    public DbSet<TransferEntity> Transfers => Set<TransferEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnownDeviceEntity>().HasKey(x => x.DeviceId);
        modelBuilder.Entity<DeviceListEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<DeviceListEntity>().HasIndex(x => x.Name);
        modelBuilder.Entity<HistoryEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<HistoryEntity>().HasIndex(x => x.StartedUtc);
        modelBuilder.Entity<ChatMessageEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ChatMessageEntity>().HasIndex(x => x.PeerDeviceId);
        modelBuilder.Entity<ContactEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<TransferEntity>().HasKey(x => x.Id);
    }

    public void EnsureDatabase()
    {
        Database.EnsureCreated();
        try
        {
            if (!Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Transfers') WHERE name = 'BrowserDestination'").Any())
                Database.ExecuteSqlRaw("ALTER TABLE Transfers ADD COLUMN BrowserDestination TEXT NULL;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Already upgraded. Other migration failures must not be hidden.
        }
        try
        {
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        }
        catch
        {
            // WAL is an optimization; the database remains usable without it.
        }

        try
        {
            // Upgrade databases created before Contact.DeviceIds existed.
            Database.ExecuteSqlRaw("ALTER TABLE Contacts ADD COLUMN DeviceIds TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // The column already exists on new databases.
        }

        using (var transaction = Database.BeginTransaction())
        {
            var columns = Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Contacts')")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!columns.Contains("Company")) Database.ExecuteSqlRaw("ALTER TABLE Contacts ADD COLUMN Company TEXT NOT NULL DEFAULT '';");
            if (!columns.Contains("Email")) Database.ExecuteSqlRaw("ALTER TABLE Contacts ADD COLUMN Email TEXT NOT NULL DEFAULT '';");
            if (!columns.Contains("Phone")) Database.ExecuteSqlRaw("ALTER TABLE Contacts ADD COLUMN Phone TEXT NOT NULL DEFAULT '';");
            if (!columns.Contains("IsFavorite")) Database.ExecuteSqlRaw("ALTER TABLE Contacts ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0;");
            transaction.Commit();
        }

        try
        {
            // Upgrade databases created before custom device lists existed.
            Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS "DeviceLists" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_DeviceLists" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "CreatedUtc" TEXT NOT NULL
                );
                """);
            Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_DeviceLists_Name\" ON \"DeviceLists\" (\"Name\");");
        }
        catch
        {
            // The table already exists or the database is read-only.
        }

        try
        {
            Database.ExecuteSqlRaw("ALTER TABLE KnownDevices ADD COLUMN ListId TEXT NULL;");
        }
        catch
        {
            // The column already exists on new databases.
        }

        try
        {
            Database.ExecuteSqlRaw("ALTER TABLE KnownDevices ADD COLUMN ProtectedPassword TEXT NULL;");
        }
        catch
        {
            // The column already exists on new databases.
        }
    }
}

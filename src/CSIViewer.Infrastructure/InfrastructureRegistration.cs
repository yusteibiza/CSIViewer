using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CSIViewer.Application;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Infrastructure.Persistence;

namespace CSIViewer.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddCSIViewerInfrastructure(this IServiceCollection services)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        services.AddDbContextFactory<ClientDbContext>(options =>
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}"));
        services.AddSingleton<IIdentityStore, DpapiIdentityStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ICustomPasswordStore, DpapiCustomPasswordStore>();
        services.AddSingleton<IKnownDeviceRepository, KnownDeviceRepository>();
        services.AddSingleton<IDeviceListRepository, DeviceListRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<IChatRepository, ChatRepository>();
        services.AddSingleton<ITransferRepository, TransferRepository>();
        services.AddSingleton<IContactRepository, ContactRepository>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<LoggingOptions>();
        return services;
    }
}

public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<ClientDbContext> _factory;

    public DatabaseInitializer(IDbContextFactory<ClientDbContext> factory) => _factory = factory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.EnsureDatabase();
        await MigrateLegacyGroupsAsync(db, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Older versions stored a free-text group name on each device. Turn those
    /// values into first-class custom lists so they remain visible and useful.
    /// </summary>
    private static async Task MigrateLegacyGroupsAsync(ClientDbContext db, CancellationToken cancellationToken)
    {
        var pending = await db.KnownDevices
            .Where(x => x.GroupName != "" && x.ListId == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var existing = await db.DeviceLists
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byName = new Dictionary<string, DeviceListEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in existing)
        {
            byName.TryAdd(list.Name, list);
        }

        foreach (var device in pending)
        {
            var name = device.GroupName.Trim();
            if (name.Length == 0)
            {
                device.GroupName = "";
                continue;
            }

            if (!byName.TryGetValue(name, out var list))
            {
                list = new DeviceListEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    CreatedUtc = DateTimeOffset.UtcNow
                };
                db.DeviceLists.Add(list);
                byName[name] = list;
            }

            device.ListId = list.Id;
            // The list is now the single source of truth.
            device.GroupName = "";
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

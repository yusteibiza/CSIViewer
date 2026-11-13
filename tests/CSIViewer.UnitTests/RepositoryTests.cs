using Microsoft.EntityFrameworkCore;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Persistence;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class RepositoryTests
{
    [TestMethod]
    public async Task ConnectionUpdatesPreserveExplicitPasswordRemovalAndDoNotRegressLastSeenOrKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-password-remove-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext()) db.EnsureDatabase();
            var repository = new KnownDeviceRepository(factory);
            var original = new KnownDevice("123456789", "Alias", "Descripción", "", true, false,
                DateTimeOffset.UtcNow, "saved-key", "list-1", "opaque-test-password-blob");
            await repository.UpsertAsync(original);
            await repository.UpsertAsync(original with { ProtectedPassword = null });
            var connected = await repository.RecordConnectionAsync(original.DeviceId, "Remote", original.LastSeenUtc.AddDays(-1), "");
            Assert.IsNull(connected.ProtectedPassword, "An explicitly removed password must not reappear.");
            Assert.AreEqual(original.LastSeenUtc, connected.LastSeenUtc);
            Assert.AreEqual(original.PublicKey, connected.PublicKey);
            Assert.IsNull((await repository.GetAllAsync()).Single().ProtectedPassword);
        }
        finally
        {
            TryDelete(path); TryDelete(path + "-shm"); TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    public async Task FirstConnectionCreatesDeviceWithoutSavingAuthenticationPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-password-new-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext()) db.EnsureDatabase();
            var repository = new KnownDeviceRepository(factory);
            var connected = await repository.RecordConnectionAsync("123456789", "Nuevo equipo", DateTimeOffset.UtcNow, "key");
            Assert.AreEqual("Nuevo equipo", connected.Alias);
            Assert.IsNull(connected.ProtectedPassword);
            Assert.IsNull((await repository.GetAllAsync()).Single().ProtectedPassword);
        }
        finally
        {
            TryDelete(path); TryDelete(path + "-shm"); TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SessionPersistencePreservesSavedPasswordAndDeviceDetails(bool cachedInUi)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-password-session-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext()) db.EnsureDatabase();
            var repository = new KnownDeviceRepository(factory);
            var original = new KnownDevice("123456789", "Mi alias", "Mi descripción", "", true, false,
                DateTimeOffset.UtcNow.AddDays(-1), "old-key", "list-1", "opaque-test-password-blob");
            await repository.UpsertAsync(original);
            var state = new CSIViewer.Services.AppState();
            if (cachedInUi) state.KnownDevices.Add(original);
            await using var client = new CSIViewer.Services.RemoteClient(state, null!, null!, repository, null!, null!,
                null!, null!, null!, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<CSIViewer.Services.RemoteClient>.Instance);
            var persist = typeof(CSIViewer.Services.RemoteClient).GetMethod("PersistKnownDeviceAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await (Task)persist.Invoke(client, [original.DeviceId, "Nombre remoto", DateTimeOffset.UtcNow, "new-key"])!;
            var stored = (await repository.GetAllAsync()).Single();
            Assert.AreEqual(original.ProtectedPassword, stored.ProtectedPassword, "Connecting must not erase the saved password.");
            Assert.AreEqual(original.Alias, stored.Alias);
            Assert.AreEqual(original.Description, stored.Description);
            Assert.AreEqual(original.ListId, stored.ListId);
            Assert.AreEqual(original.IsFavorite, stored.IsFavorite);
            Assert.AreEqual("new-key", stored.PublicKey);
            Assert.AreEqual(original.ProtectedPassword, state.KnownDevices.Single().ProtectedPassword);
        }
        finally
        {
            TryDelete(path); TryDelete(path + "-shm"); TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    public async Task TransferDestinationUpgradePreservesLegacyRecordsAndRoundTripsNewContext()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-browser-repo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
                db.Transfers.Add(new TransferEntity { Id = "legacy", FileName = "old.txt", UpdatedUtc = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Transfers DROP COLUMN BrowserDestination;");
            }
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
                db.EnsureDatabase(); // Idempotent on already upgraded databases.
            }
            var repository = new TransferRepository(factory);
            var old = (await repository.GetRecentAsync()).Single();
            Assert.AreEqual("old.txt", old.FileName);
            Assert.IsNull(old.BrowserDestination);
            old.BrowserDestination = Guid.NewGuid() + "|" + Guid.NewGuid().ToString("N");
            await repository.UpsertAsync(old);
            Assert.AreEqual(old.BrowserDestination, (await repository.GetRecentAsync()).Single().BrowserDestination);
        }
        finally
        {
            TryDelete(path); TryDelete(path + "-shm"); TryDelete(path + "-wal");
        }
    }

    private sealed class TestFactory : IDbContextFactory<ClientDbContext>
    {
        private readonly DbContextOptions<ClientDbContext> _options;

        public TestFactory(string path)
        {
            _options = new DbContextOptionsBuilder<ClientDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public ClientDbContext CreateDbContext() => new(_options);

        public Task<ClientDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    [TestMethod]
    public async Task KnownDevices_OrderByLastSeen_WorksWithSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-repo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
            }

            var repo = new KnownDeviceRepository(factory);
            await repo.UpsertAsync(new KnownDevice("111111111", "A", "", "", false, false,
                DateTimeOffset.UtcNow.AddMinutes(-10), "key-a"));
            await repo.UpsertAsync(new KnownDevice("222222222", "B", "", "", true, true,
                DateTimeOffset.UtcNow, "key-b"));

            var devices = await repo.GetAllAsync();
            Assert.AreEqual(2, devices.Count);
            Assert.AreEqual("222222222", devices[0].DeviceId);
            Assert.IsTrue(devices[0].IsFavorite);

            await repo.DeleteAsync("111111111");
            Assert.AreEqual(1, (await repo.GetAllAsync()).Count);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    public async Task DeviceLists_GroupDevicesAndSurviveListDeletion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-repo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
            }

            var lists = new DeviceListRepository(factory);
            var devices = new KnownDeviceRepository(factory);
            var list = new DeviceList("list-1", "Clientes", DateTimeOffset.UtcNow);
            await lists.UpsertAsync(list);
            await devices.UpsertAsync(new KnownDevice(
                "333333333", "Cliente", "Equipo de cliente", "", false, false,
                DateTimeOffset.UtcNow, "key-c", list.Id));

            var storedLists = await lists.GetAllAsync();
            Assert.AreEqual(1, storedLists.Count);
            Assert.AreEqual("Clientes", storedLists[0].Name);

            var storedDevices = await devices.GetAllAsync();
            Assert.AreEqual(1, storedDevices.Count);
            Assert.AreEqual(list.Id, storedDevices[0].ListId);

            await lists.DeleteAsync(list.Id);
            Assert.AreEqual(0, (await lists.GetAllAsync()).Count);
            storedDevices = await devices.GetAllAsync();
            Assert.AreEqual(1, storedDevices.Count);
            Assert.IsNull(storedDevices[0].ListId);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    public async Task LegacyGroupNames_AreMigratedToCustomLists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-repo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
            }

            var devices = new KnownDeviceRepository(factory);
            await devices.UpsertAsync(new KnownDevice(
                "444444444", "Equipo antiguo", "", "Casa", false, false,
                DateTimeOffset.UtcNow, "key-d"));

            await new DatabaseInitializer(factory).InitializeAsync();

            var lists = new DeviceListRepository(factory);
            var storedLists = await lists.GetAllAsync();
            Assert.AreEqual(1, storedLists.Count);
            Assert.AreEqual("Casa", storedLists[0].Name);

            var storedDevice = (await devices.GetAllAsync()).Single();
            Assert.AreEqual(storedLists[0].Id, storedDevice.ListId);
            Assert.AreEqual("", storedDevice.GroupName);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [TestMethod]
    public async Task ChatAndHistory_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-repo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestFactory(path);
            await using (var db = factory.CreateDbContext())
            {
                db.EnsureDatabase();
            }

            var chat = new ChatRepository(factory);
            var message = new ChatMessage("m1", "222222222", "111111111", "hola", DateTimeOffset.UtcNow,
                true, ChatDeliveryState.Sent);
            await chat.AddAsync(message);
            await chat.AddAsync(message); // duplicate id must be idempotent

            var messages = await chat.GetThreadAsync("222222222");
            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual("hola", messages[0].Text);

            var history = new HistoryRepository(factory);
            await history.AddAsync(new ConnectionHistoryEntry(
                "h1", "222222222", "Equipo B", ConnectionDirection.Outgoing,
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1), TransportKind.Relay, ConnectionResult.Success,
                "fin", null));
            var entries = await history.GetRecentAsync();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("222222222", entries[0].RemoteDeviceId);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp cleanup is best effort.
        }
    }
}

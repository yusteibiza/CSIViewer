using CSIViewer.Server;

namespace CSIViewer.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ServerHostLifecycleTests
{
    private static ServerRuntimeSettings Settings()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR")),
            "Run with isolated CSIVIEWER_DATA_DIR; never use the user's server directory.");
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-server-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new ServerRuntimeSettings
        {
            DatabasePath = Path.Combine(root, "server.db"),
            ExplicitUrl = "http://127.0.0.1:0",
            EnrollmentCode = "Synthetic-audit-enrollment-1234",
            EnableUpnp = false,
            EnableStunServer = false
        };
    }

    [TestMethod]
    public async Task DisposeIsIdempotentAndPreventsRestart()
    {
        var host = new ServerHost();
        await host.DisposeAsync();
        await host.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => host.StartAsync(new ServerRuntimeSettings()));
    }

    [TestMethod]
    public async Task StartStopRestartAndConcurrentStopReleaseListener()
    {
        var settings = Settings();
        await using var host = new ServerHost();
        try
        {
            await host.StartAsync(settings);
            Assert.IsTrue(host.IsRunning);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync(host.ActiveUrl + "/api/health");
            response.EnsureSuccessStatusCode();
            await Task.WhenAll(host.StopAsync(), host.StopAsync()).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsFalse(host.IsRunning);
            Assert.IsNull(host.ActiveUrl);
            await host.StartAsync(settings);
            Assert.IsTrue(host.IsRunning);
            await host.StopAsync();
        }
        finally
        {
            await host.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(settings.DatabasePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task CanceledStartDoesNotPreventLaterStart()
    {
        var settings = Settings();
        await using var host = new ServerHost();
        try
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => host.StartAsync(settings, cancellationToken: canceled.Token));
            Assert.IsFalse(host.IsRunning);
            await host.StartAsync(settings);
            await host.StopAsync();
        }
        finally
        {
            await host.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(settings.DatabasePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task OccupiedPortFailureCanBeRetried()
    {
        var settings = Settings();
        using var occupied = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        occupied.Start();
        settings.ExplicitUrl = "http://127.0.0.1:" + ((System.Net.IPEndPoint)occupied.LocalEndpoint).Port;
        await using var host = new ServerHost();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => host.StartAsync(settings));
            Assert.IsFalse(host.IsRunning);
            Assert.IsNull(host.ActiveUrl);
            occupied.Stop();
            await host.StartAsync(settings);
            Assert.IsTrue(host.IsRunning);
        }
        finally
        {
            await host.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(settings.DatabasePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task HashedEnrollmentAuthenticatesWithoutRecoverableCode()
    {
        var settings = Settings();
        var secret = settings.EnrollmentCode;
        var hash = CSIViewer.Security.PasswordHasher.Hash(secret, out var salt);
        settings.EnrollmentCode = "";
        settings.EnrollmentVerifier = salt + ":" + hash;
        await using var host = new ServerHost();
        try
        {
            await host.StartAsync(settings);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var identity = CSIViewer.Security.DeviceIdentity.CreateNew();
            var api = new CSIViewer.Networking.ServerApiClient(http);
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.RegisterAsync(host.ActiveUrl!, "Audit", identity.PublicKeyBase64, "wrong"));
            var device = await api.RegisterAsync(host.ActiveUrl!, "Audit", identity.PublicKeyBase64, secret);
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.DeviceId));
        }
        finally
        {
            await host.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(settings.DatabasePath)!, recursive: true);
        }
    }

    [TestMethod]
    public async Task StopClosesActiveAnonymousWebSocket()
    {
        var settings = Settings();
        await using var host = new ServerHost();
        using var socket = new System.Net.WebSockets.ClientWebSocket();
        try
        {
            await host.StartAsync(settings);
            var uri = new UriBuilder(host.ActiveUrl!) { Scheme = "ws", Path = "/ws" }.Uri;
            await socket.ConnectAsync(uri, CancellationToken.None);
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(25));
            Assert.IsFalse(host.IsRunning);
        }
        finally
        {
            socket.Abort();
            await host.StopAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(settings.DatabasePath)!, recursive: true);
        }
    }
}

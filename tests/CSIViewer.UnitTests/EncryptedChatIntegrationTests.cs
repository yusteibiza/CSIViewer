using System.Reflection;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Media;
using CSIViewer.Protocol;
using CSIViewer.Server;
using CSIViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CSIViewer.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class EncryptedChatIntegrationTests
{
    [TestMethod]
    public async Task TwoIsolatedPeersAuthenticateExchangeUnicodeAndEndSession()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR")));
        var previous = AppPaths.DataDirectory;
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-chat-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stateGate = new object();
        await using var server = new ServerHost();
        ServiceProvider? hostProvider = null;
        ServiceProvider? viewerProvider = null;
        try
        {
            await server.StartAsync(new ServerRuntimeSettings
            {
                DatabasePath = Path.Combine(root, "server.db"), ExplicitUrl = "http://127.0.0.1:0",
                EnrollmentCode = "Synthetic-integration-1234", EnableUpnp = false, EnableStunServer = false
            });
            async Task<ServiceProvider> Peer(string name, bool incoming)
            {
                var data = Path.Combine(root, name);
                Directory.CreateDirectory(data);
                AppPaths.SetDataDirectory(data);
                await new JsonSettingsStore(data).SaveAsync(new AppSettings
                {
                    ServerUrl = server.ActiveUrl!, EnrollmentCode = "Synthetic-integration-1234", DeviceName = name,
                    AutoConnect = true, AllowIncomingConnections = incoming, PreferDirectLan = false,
                    PreferModernCapture = false, StartWithWindows = false, StoreConnectionHistory = false,
                    NotificationsEnabled = false
                });
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
                services.AddCSIViewerInfrastructure();
                services.AddCSIViewerClient();
                services.AddSingleton<IConsentService>(new ChatOnlyConsent());
                services.AddSingleton<INotificationService>(new NoNotifications());
                var provider = services.BuildServiceProvider();
                try
                {
                    var state = provider.GetRequiredService<AppState>();
                    state.UiInvoker = action => { lock (stateGate) action(); };
                    var client = provider.GetRequiredService<RemoteClient>();
                    var backend = new NoDesktopAccess();
                    typeof(RemoteClient).GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, backend);
                    typeof(RemoteClient).GetField("_capture", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, backend);
                    await provider.GetRequiredService<ClientBootstrap>().InitializeAsync();
                    await WaitAsync(() => state.IsServerOnline);
                    return provider;
                }
                catch { await provider.DisposeAsync(); throw; }
            }
            hostProvider = await Peer("Synthetic host", true);
            viewerProvider = await Peer("Synthetic viewer", false);
            var host = hostProvider.GetRequiredService<RemoteClient>();
            var viewer = viewerProvider.GetRequiredService<RemoteClient>();
            var hostState = hostProvider.GetRequiredService<AppState>();
            var viewerState = viewerProvider.GetRequiredService<AppState>();
            var connected = await viewer.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, SessionPermissions.Chat, true);
            Assert.IsTrue(connected.IsSuccess, connected.Error?.Code);
            await WaitAsync(() => hostState.IsSessionActive && viewerState.IsSessionActive);
            Assert.IsFalse(viewer.CanControlRemote);
            await Task.WhenAll(viewer.SendChatToDeviceAsync(hostState.DeviceId, "Unicode desde A 😊"),
                host.SendChatToDeviceAsync(viewerState.DeviceId, "Unicode depuis B àç"));
            await WaitAsync(() => hostState.ChatMessages.Any(message => !message.IsMine && message.Text == "Unicode desde A 😊") &&
                                  viewerState.ChatMessages.Any(message => !message.IsMine && message.Text == "Unicode depuis B àç"));
            await viewer.EndCurrentSessionsAsync("synthetic audit end");
            await WaitAsync(() => !hostState.IsSessionActive && !viewerState.IsSessionActive);
            Assert.IsFalse(viewer.CanControlRemote);

            async Task WaitAsync(Func<bool> condition)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (true)
                {
                    lock (stateGate) { if (condition()) return; }
                    await Task.Delay(25, timeout.Token);
                }
            }
        }
        finally
        {
            if (viewerProvider is not null) await viewerProvider.DisposeAsync();
            if (hostProvider is not null) await hostProvider.DisposeAsync();
            await server.StopAsync();
            AppPaths.SetDataDirectory(previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ChatOnlyConsent : IConsentService
    {
        public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsentDecision(true, SessionPermissions.Chat));
        public Task<FileReceiveDecision> RequestFileReceiveAsync(string device, string name, long size, bool directory, bool overwrite, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileReceiveDecision.Reject);
    }

    private sealed class NoNotifications : INotificationService
    {
        public void Show(string title, string message) { }
    }

    private sealed class NoDesktopAccess : IHostInput, IHostCapture
    {
        public bool Send(InputEventPayload input, MonitorBounds? monitor = null) => throw new AssertFailedException("Native input forbidden.");
        public bool Send(InputEventPayload input, MonitorBounds? monitor, out InputInjectionDiagnostics? diagnostics) => throw new AssertFailedException("Native input forbidden.");
        public int ReleaseAll() => 0;
        public void ClearPressedState() { }
        public void Start(MonitorInfo monitor, int fps, int maxWidth, int jpegQuality, bool encodeJpeg, Action<CaptureFrame> onFrame, Action<Exception>? onError = null) => throw new AssertFailedException("Desktop capture forbidden.");
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

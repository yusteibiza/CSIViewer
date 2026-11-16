using System.Reflection;
using System.Text.Json;
using CSIViewer.Core;
using CSIViewer.Media;
using CSIViewer.Protocol;
using CSIViewer.Security;
using CSIViewer.Services;
using Microsoft.Extensions.Logging;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class RevocationStressTests
{
    [TestMethod]
    public async Task DelayedInputAndReplayedControlCannotActAfterFullTeardown()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        using var received = new CountdownEvent(8);
        using var release = new ManualResetEventSlim();
        var logger = new PausingLogger(received, release);
        var state = new AppState();
        state.Settings.StoreConnectionHistory = false;
        state.Settings.NotificationsEnabled = false;
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-revocation-" + Guid.NewGuid().ToString("N"));
        state.Settings.DownloadFolder = root;
        await using var client = new RemoteClient(state, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, logger);
        var input = new CountingInput();
        typeof(RemoteClient).GetField("_input", flags)!.SetValue(client, input);
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        var id = Guid.NewGuid();
        var keys = new SessionKeys(new byte[32], new byte[32], new byte[32]);
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(session, value);
        Set("Id", id); Set("RemoteDeviceId", "123456789"); Set("RemoteName", "Synthetic");
        Set("Permissions", SessionPermissions.ViewScreen | SessionPermissions.ControlInput | SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles);
        Set("IsController", false); Set("ControlOwner", true); Set("StartedUtc", DateTimeOffset.UtcNow);
        Set("Handshake", new SessionHandshake()); Set("Crypto", new SessionCrypto(id, false, keys));
        type.GetMethod("MarkConnected")!.Invoke(session, null);
        var mapField = typeof(RemoteClient).GetField("_sessions", flags)!;
        mapField.FieldType.GetMethod("TryAdd")!.Invoke(mapField.GetValue(client), [id, session]);
        var hostHandler = typeof(RemoteClient).GetMethod("HandleHostSessionFrameAsync", flags)!;
        var binaryHandler = typeof(RemoteClient).GetMethod("HandleBinaryAsync", flags)!;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new InputEventPayload("mouse", "move", 10, 20, 0, 0, 0, 0, false, TraceSampled: true), ProtocolJson.Options);
        using var sender = new SessionCrypto(id, true, keys);
        var replayedDirectory = sender.Encrypt(SessionFrameKind.Control, ControlJson.Create("file.directory", new FileDirectoryPayload("not-authorized")));
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
            ((Task)hostHandler.Invoke(client, [session, SessionFrameKind.Input, payload])!).GetAwaiter().GetResult())).ToArray();
        foreach (var thread in threads) thread.Start();
        try
        {
            Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(10)), "All eight inputs must be paused before injection.");
            await (Task)typeof(RemoteClient).GetMethod("EndSessionByIdAsync", flags)!
                .Invoke(client, [id, "synthetic revoked", false, ConnectionState.Disconnected])!;
        }
        finally
        {
            release.Set();
            foreach (var thread in threads) Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        }
        await Task.WhenAll(Enumerable.Range(0, 2000).Select(_ => Task.Run(async () =>
        {
            await (Task)hostHandler.Invoke(client, [session, SessionFrameKind.Input, payload])!;
            await (Task)binaryHandler.Invoke(client, [replayedDirectory])!;
        })));
        Assert.AreEqual(0, input.Sent);
        Assert.IsTrue(input.Released > 0);
        Assert.IsFalse(Directory.Exists(root));
        foreach (var name in new[] { "_sessions", "_incomingFiles", "_outgoingFiles" })
        {
            var value = typeof(RemoteClient).GetField(name, flags)!.GetValue(client)!;
            Assert.AreEqual(0, (int)value.GetType().GetProperty("Count")!.GetValue(value)!);
        }
    }

    private sealed class CountingInput : IHostInput
    {
        private int _sent, _released;
        public int Sent => Volatile.Read(ref _sent);
        public int Released => Volatile.Read(ref _released);
        public bool Send(InputEventPayload input, MonitorBounds? monitor = null) { Interlocked.Increment(ref _sent); return true; }
        public bool Send(InputEventPayload input, MonitorBounds? monitor, out InputInjectionDiagnostics? diagnostics) { diagnostics = null; return Send(input, monitor); }
        public int ReleaseAll() { Interlocked.Increment(ref _released); return 0; }
        public void ClearPressedState() { }
    }

    private sealed class PausingLogger(CountdownEvent entered, ManualResetEventSlim release) : ILogger<RemoteClient>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!formatter(state, exception).StartsWith("Input received.", StringComparison.Ordinal)) return;
            entered.Signal();
            release.Wait(TimeSpan.FromSeconds(15));
        }
    }
}

using System.Reflection;
using System.Text.Json;
using CSIViewer.Core;
using CSIViewer.Media;
using CSIViewer.Protocol;
using CSIViewer.Security;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class RemoteInputAuthorizationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static byte[] Input() => JsonSerializer.SerializeToUtf8Bytes(
        new InputEventPayload("mouse", "move", 100, 100, 0, 0, 0, 0, false), ProtocolJson.Options);

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(false, false, true)]
    public async Task InputBackendRequiresLiveAuthorizedSession(bool disconnected, bool revoking, bool registered)
    {
        // Replace the internally constructed native backend before dispatch.
        await using var client = new RemoteClient(new AppState(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);
        var input = new RecordingInput();
        typeof(RemoteClient).GetField("_input", PrivateInstance)!.SetValue(client, input);
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        var id = Guid.NewGuid();
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(session, value);
        Set("Id", id); Set("RemoteDeviceId", "123456789"); Set("RemoteName", "QA");
        Set("Permissions", SessionPermissions.ViewScreen | SessionPermissions.ControlInput);
        Set("IsController", false); Set("ControlOwner", true);
        Set("Handshake", new SessionHandshake());
        Set("Crypto", new SessionCrypto(id, false, new SessionKeys(new byte[32], new byte[32], new byte[32])));
        type.GetMethod("MarkConnected")!.Invoke(session, null);
        if (disconnected) type.GetMethod("MarkDisconnecting")!.Invoke(session, null);
        if (revoking) typeof(RemoteClient).GetField("_revokingAccess", PrivateInstance)!.SetValue(client, true);
        if (registered)
        {
            var field = typeof(RemoteClient).GetField("_sessions", PrivateInstance)!;
            var sessions = field.GetValue(client)!;
            field.FieldType.GetMethod("TryAdd")!.Invoke(sessions, [id, session]);
        }

        await (Task)typeof(RemoteClient).GetMethod("HandleHostSessionFrameAsync", PrivateInstance)!
            .Invoke(client, [session, SessionFrameKind.Input, Input()])!;
        Assert.AreEqual(registered && !disconnected && !revoking ? 1 : 0, input.Sent);
    }

    [TestMethod]
    public async Task AuthenticationFallbackCannotCreateDirectoriesWithoutLiveSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "csiviewer-stale-control-" + Guid.NewGuid().ToString("N"));
        var state = new AppState();
        state.Settings.DownloadFolder = root;
        await using var client = new RemoteClient(state, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);
        typeof(RemoteClient).GetField("_permissions", PrivateInstance)!.SetValue(client,
            SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles);
        try
        {
            var payload = ControlJson.Create("file.directory", new FileDirectoryPayload("unauthorized"));
            await (Task)typeof(RemoteClient).GetMethod("HandleControllerFrameAsync", PrivateInstance)!
                .Invoke(client, [SessionFrameKind.Control, payload, SessionPermissions.SendFiles | SessionPermissions.ReceiveFiles, null])!;
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "unauthorized")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ControllerFallbackCannotInjectInputWithoutHostSession()
    {
        await using var client = new RemoteClient(new AppState(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);
        var input = new RecordingInput();
        typeof(RemoteClient).GetField("_input", PrivateInstance)!.SetValue(client, input);
        await (Task)typeof(RemoteClient).GetMethod("HandleControllerFrameAsync", PrivateInstance)!
            .Invoke(client, [SessionFrameKind.Input, Input(), SessionPermissions.None, null])!;
        Assert.AreEqual(0, input.Sent);
    }

    private sealed class RecordingInput : IHostInput
    {
        public int Sent { get; private set; }
        public bool Send(InputEventPayload input, MonitorBounds? monitor = null)
        {
            Sent++;
            return true;
        }
        public bool Send(InputEventPayload input, MonitorBounds? monitor, out InputInjectionDiagnostics? diagnostics)
        {
            diagnostics = null;
            return Send(input, monitor);
        }
        public int ReleaseAll() => 0;
        public void ClearPressedState() { }
    }
}

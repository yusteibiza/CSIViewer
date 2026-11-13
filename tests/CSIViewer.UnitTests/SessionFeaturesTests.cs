using System.Reflection;
using CSIViewer.Core;
using CSIViewer.Protocol;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionFeaturesTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public async Task FeatureEnableCannotExceedGrantedPermissions()
    {
        await using var client = NewClient();
        var session = NewSession(SessionPermissions.Chat);
        typeof(RemoteClient).GetMethod("SetSessionFeatures", PrivateInstance)!.Invoke(client, [session, true, true]);
        Assert.IsFalse((bool)session.GetType().GetField("AudioEnabled")!.GetValue(session)!);
        Assert.IsFalse((bool)session.GetType().GetField("ClipboardEnabled")!.GetValue(session)!);
        Assert.IsNull(session.GetType().GetField("AudioStream")!.GetValue(session));
    }

    [TestMethod]
    [DataRow(SessionFrameKind.Audio)]
    [DataRow(SessionFrameKind.Clipboard)]
    public async Task DataWithoutOptInIsDroppedWithoutTouchingDevicesOrClipboard(SessionFrameKind kind)
    {
        await using var client = NewClient();
        var session = NewSession(SessionPermissions.All);
        var payload = kind == SessionFrameKind.Audio
            ? AudioPacket.Create(48000, 2, 32, true, new byte[80]) : "synthetic text"u8.ToArray();
        var handled = typeof(RemoteClient).GetMethod("HandleFeatureFrame", PrivateInstance)!.Invoke(client, [session, kind, payload]);
        Assert.AreEqual(true, handled);
        Assert.AreEqual(0L, client.AudioPacketsReceived);
        Assert.AreEqual(0L, client.ClipboardUpdatesReceived);
    }

    [TestMethod]
    public async Task MissingSessionDoesNotEnableFeatures()
    {
        await using var client = NewClient();
        await client.ConfigureSessionFeaturesAsync(true, true);
        Assert.IsFalse(client.IsAudioEnabled);
        Assert.IsFalse(client.IsClipboardEnabled);
    }

    [TestMethod]
    public async Task IncomingClipboardFloodKeepsOneQueuedApplyAndOnlyLatestText()
    {
        var actions = new List<Action>();
        await using var client = NewClient(actions.Add);
        var session = NewSession(SessionPermissions.Clipboard);
        session.GetType().GetField("ClipboardEnabled")!.SetValue(session, true);
        var handler = typeof(RemoteClient).GetMethod("HandleFeatureFrame", PrivateInstance)!;
        for (var i = 0; i < 100; i++)
            handler.Invoke(client, [session, SessionFrameKind.Clipboard, System.Text.Encoding.UTF8.GetBytes("QA-" + i)]);
        Assert.AreEqual(1, actions.Count);
        Assert.AreEqual("QA-99", session.GetType().GetField("PendingClipboardText")!.GetValue(session));
    }

    private static RemoteClient NewClient(Action<Action>? dispatcher = null) => new(new AppState { UiInvoker = dispatcher ?? (_ => { }) }, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);

    private static object NewSession(SessionPermissions permissions)
    {
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("Permissions")!.SetValue(session, permissions);
        type.GetProperty("IsController")!.SetValue(session, true);
        return session;
    }
}

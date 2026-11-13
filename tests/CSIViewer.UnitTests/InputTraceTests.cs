using System.Text.Json;
using CSIViewer.Protocol;
using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class InputTraceTests
{
    [TestMethod]
    public void LegacyInput_DeserializesWithoutTrace()
    {
        const string json = """
            {"kind":"mouse","action":"leftDown","x":123,"y":456,"button":0,"delta":0,"keyCode":0,"scanCode":0,"down":true}
            """;
        var input = JsonSerializer.Deserialize<InputEventPayload>(json, ProtocolJson.Options);
        Assert.IsNotNull(input);
        Assert.IsNull(input.TraceId);
        Assert.IsFalse(input.TraceSampled);
        Assert.IsTrue(MessageValidation.IsValidInput(input));
        Assert.AreEqual(123, input.X);
        Assert.IsTrue(input.HasPosition);
    }

    [TestMethod]
    public void MouseTrace_SurvivesEncryptionAndSerialization()
    {
        var id = Guid.NewGuid();
        var keys = new SessionKeys(new byte[32], Enumerable.Repeat((byte)1, 32).ToArray(), new byte[32]);
        using var controller = new SessionCrypto(id, true, keys);
        using var host = new SessionCrypto(id, false, keys);
        var input = new InputEventPayload("mouse", "leftDown", 123, 456, 0, 0, 0, 0, true,
            TraceId: Guid.NewGuid().ToString("N"), TraceSampled: true);
        var json = JsonSerializer.SerializeToUtf8Bytes(input, ProtocolJson.Options);
        var frame = controller.Encrypt(SessionFrameKind.Input, json);
        Assert.IsTrue(host.TryDecrypt(frame, out var kind, out var plaintext));
        Assert.AreEqual(SessionFrameKind.Input, kind);
        Assert.AreEqual(input, JsonSerializer.Deserialize<InputEventPayload>(plaintext, ProtocolJson.Options));
        Assert.IsTrue(MessageValidation.IsValidInput(input));
    }
}

using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class InputEventDiagnosticsTests
{
    [TestMethod]
    public void TextIsRedactedWithoutChangingTheInputPayload()
    {
        var input = new InputEventPayload("text", "synthetic-secret-not-a-real-password", 0, 0, 0, 0, 0, 0, false);
        Assert.AreEqual("[redacted text]", InputEventDiagnostics.SafeAction(input));
        Assert.AreEqual("synthetic-secret-not-a-real-password", input.Action);
    }

    [TestMethod]
    [DataRow("mouse", "leftDown")]
    [DataRow("key", "down")]
    public void NonTextActionRemainsUsefulForDiagnostics(string kind, string action)
    {
        var input = new InputEventPayload(kind, action, 0, 0, 0, 0, 0, 0, false);
        Assert.AreEqual(action, InputEventDiagnostics.SafeAction(input));
    }

    [TestMethod]
    public void MissingPayloadDoesNotLeakOrThrow() => Assert.IsNull(InputEventDiagnostics.SafeAction(null));
}

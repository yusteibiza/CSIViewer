using CSIViewer.Protocol;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class PressedInputStateTests
{
    private static InputEventPayload Key(int scanCode, bool down) =>
        new("key", down ? "keyDown" : "keyUp", 0, 0, 0, 0, 0, scanCode, down);

    private static InputEventPayload Mouse(string action) =>
        new("mouse", action, 0, 0, 0, 0, 0, 0, false);

    [TestMethod]
    public void TracksAndReleasesKeysAndButtons()
    {
        var state = new PressedInputState();
        state.Observe(Key(30, down: true));
        state.Observe(Key(31, down: true));
        state.Observe(Mouse("leftDown"));
        state.Observe(Mouse("rightDown"));

        Assert.AreEqual(2, state.PressedKeyCount);
        Assert.AreEqual(2, state.PressedButtonCount);

        var releases = state.GetReleaseEvents();

        Assert.AreEqual(4, releases.Count);
        Assert.IsTrue(releases.Any(x => x.Kind == "key" && x.ScanCode == 30 && !x.Down));
        Assert.IsTrue(releases.Any(x => x.Kind == "key" && x.ScanCode == 31 && !x.Down));
        Assert.IsTrue(releases.Any(x => x.Action == "leftUp"));
        Assert.IsTrue(releases.Any(x => x.Action == "rightUp"));
        Assert.AreEqual(0, state.PressedKeyCount);
        Assert.AreEqual(0, state.PressedButtonCount);
        Assert.AreEqual(0, state.GetReleaseEvents().Count);
    }

    [TestMethod]
    public void ReleasingAKey_RemovesItFromThePendingSet()
    {
        var state = new PressedInputState();
        state.Observe(Key(30, down: true));
        state.Observe(Key(30, down: false));

        Assert.AreEqual(0, state.PressedKeyCount);
        Assert.AreEqual(0, state.GetReleaseEvents().Count);
    }

    [TestMethod]
    public void RepeatedDown_DoesNotDuplicateReleases()
    {
        var state = new PressedInputState();
        state.Observe(Mouse("leftDown"));
        state.Observe(Mouse("leftDown"));

        Assert.AreEqual(1, state.PressedButtonCount);
        Assert.AreEqual(1, state.GetReleaseEvents().Count);
    }

    [TestMethod]
    public void Clear_ForgetsAllPressedInput()
    {
        var state = new PressedInputState();
        state.Observe(Key(30, down: true));
        state.Observe(Mouse("middleDown"));

        state.Clear();

        Assert.AreEqual(0, state.PressedKeyCount);
        Assert.AreEqual(0, state.PressedButtonCount);
        Assert.AreEqual(0, state.GetReleaseEvents().Count);
    }
}

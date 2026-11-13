using CSIViewer.Security;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class DeviceKeyPinTests
{
    [TestMethod]
    public void FirstUse_AcceptsAnyKey()
    {
        Assert.IsTrue(DeviceKeyPin.IsConsistent(null, "new-key"));
        Assert.IsTrue(DeviceKeyPin.IsConsistent("", "new-key"));
    }

    [TestMethod]
    public void SameKey_IsAccepted()
    {
        Assert.IsTrue(DeviceKeyPin.IsConsistent("pinned-key", "pinned-key"));
    }

    [TestMethod]
    public void ChangedKey_IsRejected()
    {
        Assert.IsFalse(DeviceKeyPin.IsConsistent("pinned-key", "different-key"));
    }
}

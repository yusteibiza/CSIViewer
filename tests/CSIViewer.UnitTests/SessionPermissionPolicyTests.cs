using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionPermissionPolicyTests
{
    [TestMethod]
    public void Controller_CanSendOnlyWithSendFiles()
    {
        Assert.IsTrue(SessionPermissionPolicy.CanSendFiles(
            localIsController: true,
            SessionPermissions.SendFiles));
        Assert.IsFalse(SessionPermissionPolicy.CanSendFiles(
            localIsController: true,
            SessionPermissions.ReceiveFiles));
    }

    [TestMethod]
    public void Host_CanSendOnlyWithReceiveFiles()
    {
        Assert.IsTrue(SessionPermissionPolicy.CanSendFiles(
            localIsController: false,
            SessionPermissions.ReceiveFiles));
        Assert.IsFalse(SessionPermissionPolicy.CanSendFiles(
            localIsController: false,
            SessionPermissions.SendFiles));
    }

    [TestMethod]
    public void ReceiveDirection_IsInvertedForTheHost()
    {
        Assert.IsTrue(SessionPermissionPolicy.CanReceiveFiles(
            localIsController: false,
            SessionPermissions.SendFiles));
        Assert.IsFalse(SessionPermissionPolicy.CanReceiveFiles(
            localIsController: false,
            SessionPermissions.ReceiveFiles));

        Assert.IsTrue(SessionPermissionPolicy.CanReceiveFiles(
            localIsController: true,
            SessionPermissions.ReceiveFiles));
        Assert.IsFalse(SessionPermissionPolicy.CanReceiveFiles(
            localIsController: true,
            SessionPermissions.SendFiles));
    }

    [TestMethod]
    public void Sanitize_RemovesUnknownPermissionBits()
    {
        var raw = SessionPermissions.ViewScreen |
                  SessionPermissions.Chat |
                  (SessionPermissions)0x4000;

        var sanitized = SessionPermissionPolicy.Sanitize(raw);

        Assert.AreEqual(SessionPermissions.ViewScreen | SessionPermissions.Chat, sanitized);
    }

    [TestMethod]
    public void IndependentPermissions_AreNotImplied()
    {
        Assert.IsFalse(SessionPermissionPolicy.CanControl(SessionPermissions.ViewScreen));
        Assert.IsFalse(SessionPermissionPolicy.CanChat(SessionPermissions.ControlInput));
        Assert.IsFalse(SessionPermissionPolicy.CanViewScreen(SessionPermissions.Chat));
        Assert.IsFalse(SessionPermissionPolicy.CanTransferFiles(SessionPermissions.ViewScreen));
    }
}

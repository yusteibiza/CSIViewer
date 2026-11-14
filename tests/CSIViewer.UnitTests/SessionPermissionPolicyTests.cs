using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionPermissionPolicyTests
{
    [TestMethod]
    [DataRow(SessionPermissions.ViewScreen, SessionPermissions.ViewScreen, "control_in_use", ControlRestriction.None)]
    [DataRow(SessionPermissions.All, SessionPermissions.All, "control_in_use", ControlRestriction.None)]
    [DataRow(SessionPermissions.All, SessionPermissions.ViewScreen, "control_in_use", ControlRestriction.InUse)]
    [DataRow(SessionPermissions.All, SessionPermissions.ViewScreen, "denied", ControlRestriction.NotGranted)]
    [DataRow(SessionPermissions.All, SessionPermissions.ViewScreen, null, ControlRestriction.NotGranted)]
    [DataRow(SessionPermissions.All, SessionPermissions.ViewScreen, "unknown", ControlRestriction.NotGranted)]
    public void MissingControl_IsClassifiedWithoutGrantingPermissions(SessionPermissions requested, SessionPermissions granted, string? reason, ControlRestriction expected)
    {
        Assert.AreEqual(expected, SessionPermissionPolicy.GetControlRestriction(requested, granted, reason));
    }

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

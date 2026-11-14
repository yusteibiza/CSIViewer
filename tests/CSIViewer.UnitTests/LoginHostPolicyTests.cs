using CSIViewer.Application;
using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class LoginHostPolicyTests
{
    [TestMethod]
    public void MachineEndpointNeverGrantsFilesClipboardAudioOrChat()
    {
        Assert.AreEqual(SessionPermissions.ViewScreen | SessionPermissions.ControlInput,
            LoginHostPolicy.Grant(SessionPermissions.All, false, true, true, 1, 1));
    }

    [TestMethod]
    public void ViewOnlyDoesNotAcquireControl()
    {
        Assert.AreEqual(SessionPermissions.ViewScreen,
            LoginHostPolicy.Grant(SessionPermissions.ViewScreen, false, true, true, 1, 1));
    }

    [TestMethod]
    public void ControlIncludesViewButNotOtherFeatures()
    {
        Assert.AreEqual(LoginHostPolicy.AllowedPermissions,
            LoginHostPolicy.Grant(SessionPermissions.ControlInput, false, true, true, 1, 1));
    }

    [TestMethod]
    [DataRow(true, true, true, 1, 1u)]
    [DataRow(false, false, true, 1, 1u)]
    [DataRow(false, true, false, 1, 1u)]
    [DataRow(false, true, true, 0, 1u)]
    [DataRow(false, true, true, 1, 2u)]
    [DataRow(false, true, true, 1, uint.MaxValue)]
    public void DeniesUnattendedAccessOutsideExplicitSystemConsolePolicy(bool chatOnly, bool enabled, bool system, int worker, uint console)
    {
        Assert.AreEqual(SessionPermissions.None,
            LoginHostPolicy.Grant(SessionPermissions.All, chatOnly, enabled, system, worker, console));
    }
}

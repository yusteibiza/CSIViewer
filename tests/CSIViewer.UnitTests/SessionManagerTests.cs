using CSIViewer.Core;
using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class SessionManagerTests
{
    [TestMethod]
    public void ConcurrentClients_RespectConfiguredLimit()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var accepted = 0;
        var rejected = 0;

        Parallel.For(0, 40, index =>
        {
            if (manager.TryAddPending(
                    Guid.NewGuid(),
                    $"controller-{index}",
                    SessionPermissions.ViewScreen,
                    out _))
            {
                Interlocked.Increment(ref accepted);
            }
            else
            {
                Interlocked.Increment(ref rejected);
            }
        });

        Assert.AreEqual(10, accepted);
        Assert.AreEqual(30, rejected);
        Assert.AreEqual(10, manager.PendingCount);
    }

    [TestMethod]
    public void DuplicateControllerAndSession_AreRejected()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var session = Guid.NewGuid();

        Assert.IsTrue(manager.TryAddPending(
            session, "controller-a", SessionPermissions.ViewScreen, out _));
        Assert.IsFalse(manager.TryAddPending(
            session, "controller-b", SessionPermissions.ViewScreen, out var duplicateSession));
        Assert.AreEqual("duplicate_session", duplicateSession);

        Assert.IsFalse(manager.TryAddPending(
            Guid.NewGuid(), "controller-a", SessionPermissions.ViewScreen, out var duplicateController));
        Assert.AreEqual("controller_busy", duplicateController);
    }

    [TestMethod]
    public void TenClients_OnlyOneReceivesControlLease()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();

        foreach (var id in ids)
        {
            Assert.IsTrue(manager.TryAddPending(
                id, $"controller-{id:N}", SessionPermissions.ViewScreen | SessionPermissions.ControlInput,
                out _));
        }

        var controlOwners = 0;
        var viewOnly = 0;
        foreach (var id in ids)
        {
            Assert.IsTrue(manager.TryActivate(
                id,
                SessionPermissions.ViewScreen | SessionPermissions.ControlInput,
                out var granted,
                out var owner,
                out _));
            if (owner)
            {
                controlOwners++;
                Assert.IsTrue(granted.HasFlag(SessionPermissions.ControlInput));
            }
            else
            {
                viewOnly++;
                Assert.IsFalse(granted.HasFlag(SessionPermissions.ControlInput));
            }
        }

        Assert.AreEqual(1, controlOwners);
        Assert.AreEqual(9, viewOnly);
        Assert.AreEqual(10, manager.ActiveCount);
    }

    [TestMethod]
    public void RemovingAClient_DoesNotAffectOtherSessions()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            manager.TryAddPending(id, $"controller-{id:N}", SessionPermissions.ViewScreen, out _);
            manager.TryActivate(id, SessionPermissions.ViewScreen, out _, out _, out _);
        }

        Assert.IsTrue(manager.TryRemove(ids[0], out _, out var remaining));
        Assert.AreEqual(9, remaining);
        Assert.AreEqual(9, manager.ActiveCount);
        Assert.IsTrue(manager.TryGetActive(ids[1], out _));
        Assert.IsTrue(manager.TryGetActive(ids[9], out _));
    }

    [TestMethod]
    public void RemovingControlOwner_ReleasesControlLease()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        manager.TryAddPending(first, "controller-a", SessionPermissions.ControlInput, out _);
        manager.TryActivate(first, SessionPermissions.ControlInput, out _, out var firstOwner, out _);
        manager.TryAddPending(second, "controller-b", SessionPermissions.ControlInput, out _);
        manager.TryActivate(second, SessionPermissions.ControlInput, out _, out var secondOwner, out _);

        Assert.IsTrue(firstOwner);
        Assert.IsFalse(secondOwner);
        Assert.AreEqual(first, manager.ControlOwner);

        manager.TryRemove(first, out var wasOwner, out _);
        Assert.IsTrue(wasOwner);
        Assert.IsNull(manager.ControlOwner);
        Assert.IsTrue(manager.TryGetActive(second, out var existing));
        Assert.IsFalse(existing!.RequestedPermissions.HasFlag(SessionPermissions.ControlInput));
        var third = Guid.NewGuid();
        Assert.IsTrue(manager.TryAddPending(third, "controller-c", SessionPermissions.All, out _));
        Assert.IsTrue(manager.TryActivate(third, SessionPermissions.All, out var granted, out var owner, out var reason));
        Assert.IsTrue(owner);
        Assert.IsTrue(granted.HasFlag(SessionPermissions.ControlInput));
        Assert.IsNull(reason);
        Assert.AreEqual(third, manager.ControlOwner);
    }

    [TestMethod]
    public void RemovedSession_CannotBeReactivated()
    {
        var manager = new SessionManager { MaxSessions = 10 };
        var id = Guid.NewGuid();
        Assert.IsTrue(manager.TryAddPending(
            id,
            "controller-a",
            SessionPermissions.ViewScreen,
            out _));
        Assert.IsTrue(manager.TryActivate(
            id,
            SessionPermissions.ViewScreen,
            out _,
            out _,
            out _));
        Assert.IsTrue(manager.TryRemove(id, out _, out _));

        Assert.IsFalse(manager.TryActivate(
            id,
            SessionPermissions.ViewScreen,
            out var granted,
            out var owner,
            out var reason));
        Assert.AreEqual(SessionPermissions.None, granted);
        Assert.IsFalse(owner);
        Assert.AreEqual("not_pending", reason);
    }

    [TestMethod]
    public void ManyConnectDisconnectCycles_LeaveNoResidualState()
    {
        var manager = new SessionManager { MaxSessions = 10 };

        for (var cycle = 0; cycle < 200; cycle++)
        {
            var id = Guid.NewGuid();
            Assert.IsTrue(manager.TryAddPending(
                id, $"controller-{cycle}", SessionPermissions.ViewScreen, out _));
            Assert.IsTrue(manager.TryActivate(
                id, SessionPermissions.ViewScreen, out _, out _, out _));
            Assert.IsTrue(manager.TryRemove(id, out _, out _));
        }

        Assert.AreEqual(0, manager.ActiveCount);
        Assert.AreEqual(0, manager.PendingCount);
        Assert.IsNull(manager.ControlOwner);
    }
}

using CSIViewer.Networking;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ClientHubTests
{
    private static SessionRoute Route(string controllerId, string hostId, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            ControllerId = controllerId,
            HostId = hostId
        };

    [TestMethod]
    public void ConcurrentAdmissions_RespectPerHostLimit()
    {
        var hub = new ClientHub();
        var accepted = 0;
        var rejected = 0;

        Parallel.For(0, 40, index =>
        {
            var route = Route($"controller-{index}", "host-1");
            if (hub.TryAdmitSession(route, 10) == SessionAdmissionResult.Accepted)
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
        Assert.AreEqual(10, hub.SessionsFor("host-1").Count);
    }

    [TestMethod]
    public void DuplicateSessionId_IsRejected()
    {
        var hub = new ClientHub();
        var route = Route("controller-a", "host-1");

        Assert.AreEqual(
            SessionAdmissionResult.Accepted,
            hub.TryAdmitSession(route, 10));
        Assert.AreEqual(
            SessionAdmissionResult.DuplicateSession,
            hub.TryAdmitSession(Route("controller-b", "host-1", route.Id), 10));
    }

    [TestMethod]
    public void SameController_CanOnlyHaveOneSessionPerHost()
    {
        var hub = new ClientHub();

        Assert.AreEqual(
            SessionAdmissionResult.Accepted,
            hub.TryAdmitSession(Route("controller-a", "host-1"), 10));
        Assert.AreEqual(
            SessionAdmissionResult.ControllerBusy,
            hub.TryAdmitSession(Route("controller-a", "host-1"), 10));

        // The same controller may still connect to a different host.
        Assert.AreEqual(
            SessionAdmissionResult.Accepted,
            hub.TryAdmitSession(Route("controller-a", "host-2"), 10));
    }

    [TestMethod]
    public void ConcurrentDuplicateRequests_OnlyOneIsAccepted()
    {
        var hub = new ClientHub();
        var accepted = 0;

        Parallel.For(0, 20, _ =>
        {
            if (hub.TryAdmitSession(Route("controller-a", "host-1"), 10) ==
                SessionAdmissionResult.Accepted)
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.AreEqual(1, accepted);
        Assert.AreEqual(1, hub.SessionsFor("host-1").Count);
    }

    [TestMethod]
    public void OnlySessionParticipants_CanResolveThePeer()
    {
        var route = Route("controller-a", "host-1");

        Assert.IsTrue(route.TryGetPeer("controller-a", out var controllerPeer));
        Assert.AreEqual("host-1", controllerPeer);
        Assert.IsTrue(route.TryGetPeer("host-1", out var hostPeer));
        Assert.AreEqual("controller-a", hostPeer);
        Assert.IsFalse(route.TryGetPeer("intruder", out _));
    }

    [TestMethod]
    public void SessionsAreScopedToTheOwningConnection()
    {
        var hub = new ClientHub();
        var firstConnection = Guid.NewGuid();
        var secondConnection = Guid.NewGuid();
        var first = new SessionRoute
        {
            Id = Guid.NewGuid(),
            ControllerId = "controller-a",
            HostId = "host-1",
            ControllerConnectionId = firstConnection,
            HostConnectionId = Guid.NewGuid()
        };
        var second = new SessionRoute
        {
            Id = Guid.NewGuid(),
            ControllerId = "controller-b",
            HostId = "host-2",
            ControllerConnectionId = secondConnection,
            HostConnectionId = Guid.NewGuid()
        };

        Assert.AreEqual(SessionAdmissionResult.Accepted, hub.TryAdmitSession(first, 10));
        Assert.AreEqual(SessionAdmissionResult.Accepted, hub.TryAdmitSession(second, 10));

        Assert.AreEqual(1, hub.SessionsForConnection(firstConnection).Count);
        Assert.AreEqual(first.Id, hub.SessionsForConnection(firstConnection)[0].Id);
        Assert.AreEqual(1, hub.SessionsForConnection(secondConnection).Count);

        hub.RemoveSession(first.Id);

        Assert.AreEqual(0, hub.SessionsForConnection(firstConnection).Count);
        Assert.AreEqual(1, hub.SessionsForConnection(secondConnection).Count);
    }

    [TestMethod]
    public void RemovingASession_FreesCapacity()
    {
        var hub = new ClientHub();
        var routes = Enumerable.Range(0, 10)
            .Select(index => Route($"controller-{index}", "host-1"))
            .ToArray();

        foreach (var route in routes)
        {
            Assert.AreEqual(
                SessionAdmissionResult.Accepted,
                hub.TryAdmitSession(route, 10));
        }

        Assert.AreEqual(
            SessionAdmissionResult.HostFull,
            hub.TryAdmitSession(Route("controller-extra", "host-1"), 10));

        hub.RemoveSession(routes[3].Id);

        Assert.AreEqual(
            SessionAdmissionResult.Accepted,
            hub.TryAdmitSession(Route("controller-new", "host-1"), 10));
    }
}

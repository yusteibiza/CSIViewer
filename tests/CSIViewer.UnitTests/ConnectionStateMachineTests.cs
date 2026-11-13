using CSIViewer.Core;
using CSIViewer.Services;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ConnectionStateMachineTests
{
    [TestMethod]
    public void AllowsTheNormalLifecycle()
    {
        var machine = new ConnectionStateMachine();

        Assert.IsTrue(machine.TryTransition(ConnectionState.Connecting, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Authenticating, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Connected, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Reconnecting, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Authenticating, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Connected, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Disconnecting, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Disconnected, out _));
        Assert.AreEqual(ConnectionState.Disconnected, machine.State);
    }

    [TestMethod]
    public void RejectsInvalidTransitions()
    {
        var machine = new ConnectionStateMachine();

        Assert.IsFalse(machine.TryTransition(ConnectionState.Connected, out var error));
        Assert.IsNotNull(error);
        Assert.AreEqual(ConnectionState.Disconnected, machine.State);
    }

    [TestMethod]
    public void SameState_IsIdempotent()
    {
        var machine = new ConnectionStateMachine();
        machine.TryTransition(ConnectionState.Connecting, out _);

        Assert.IsTrue(machine.TryTransition(ConnectionState.Connecting, out _));
        Assert.AreEqual(ConnectionState.Connecting, machine.State);
    }

    [TestMethod]
    public void RecoversFromFailureThroughReconnect()
    {
        var machine = new ConnectionStateMachine();
        machine.TryTransition(ConnectionState.Connecting, out _);
        machine.TryTransition(ConnectionState.Failed, out _);

        Assert.IsTrue(machine.TryTransition(ConnectionState.Reconnecting, out _));
        Assert.IsTrue(machine.TryTransition(ConnectionState.Connected, out _));
        Assert.AreEqual(ConnectionState.Connected, machine.State);
    }
}

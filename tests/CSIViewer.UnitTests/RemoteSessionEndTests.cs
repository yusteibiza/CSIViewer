using System.Reflection;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Security;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class RemoteSessionEndTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SessionStateClearsBeforeHistoryIoCompletes(bool controller)
    {
        var state = new AppState { IsSessionActive = true, IsController = controller, ActiveSessionCount = 1 };
        var history = new DeferredHistory();
        // No bootstrap/network/database is started. Only session teardown and
        // history are exercised; unrelated dependencies are deliberately absent.
        await using var client = new RemoteClient(state, null!, null!, null!, null!, history,
            null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);
        var sessionId = Guid.NewGuid();
        state.ActiveSessionId = sessionId.ToString();
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(type, nonPublic: true)!;
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(session, value);
        Set("Id", sessionId);
        Set("RemoteDeviceId", "123456789");
        Set("RemoteName", "QA");
        Set("Permissions", SessionPermissions.Chat);
        Set("IsController", controller);
        Set("StartedUtc", DateTimeOffset.UtcNow);
        Set("Handshake", new SessionHandshake());
        Set("Crypto", new SessionCrypto(sessionId, controller,
            new SessionKeys(new byte[32], new byte[32], new byte[32])));
        type.GetMethod("MarkConnected")!.Invoke(session, null);
        var field = typeof(RemoteClient).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var sessions = field.GetValue(client)!;
        Assert.IsTrue((bool)field.FieldType.GetMethod("TryAdd")!.Invoke(sessions, [sessionId, session])!);

        var close = (Task)typeof(RemoteClient)
            .GetMethod("EndSessionByIdAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(client, [sessionId, "qa-ended", false, ConnectionState.Disconnected])!;
        try
        {
            await history.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsFalse(close.IsCompleted, "History should still be waiting.");
            Assert.IsFalse(state.IsSessionActive, "A closed session must not remain visible during history I/O.");
            Assert.AreEqual(0, state.ActiveSessionCount);
            Assert.IsNull(state.ActiveSessionId);
        }
        finally
        {
            history.Release.TrySetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class DeferredHistory : IHistoryRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<ConnectionHistoryEntry>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConnectionHistoryEntry>>([]);
        public async Task AddAsync(ConnectionHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

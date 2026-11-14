using System.Reflection;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Services;
using CSIViewer.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ChatHistoryTests
{
    private const string PeerA = "111111111";
    private const string PeerB = "222222222";
    private static readonly string[] BothMessages = ["a", "b"];
    private static readonly string[] AfterDelete = ["b", "new-a"];
    private static ChatMessage Message(string id, string peer, string text = "synthetic text") =>
        new(id, peer, peer, text, DateTimeOffset.UtcNow, false, ChatDeliveryState.Received);

    [TestMethod]
    public async Task LoadingAnotherThreadDoesNotClearPreviouslyLoadedMessages()
    {
        var repo = new MemoryChats();
        repo.Messages.AddRange([Message("a", PeerA), Message("b", PeerB)]);
        var state = new AppState();
        await using var client = NewClient(state, repo);
        await client.LoadChatHistoryAsync(PeerA);
        await client.LoadChatHistoryAsync(PeerB);
        await client.LoadChatHistoryAsync(PeerA);
        CollectionAssert.AreEquivalent(BothMessages, state.ChatMessages.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task DeleteInvalidatesQueuedHistoryAndKeepsOtherPeerAndNewMessages()
    {
        var pending = new List<Action>();
        var state = new AppState { UiInvoker = pending.Add };
        state.ChatMessages.Add(Message("b", PeerB));
        var repo = new MemoryChats();
        repo.Messages.AddRange([Message("old-a", PeerA), Message("b", PeerB)]);
        await using var client = NewClient(state, repo);
        await client.LoadChatHistoryAsync(PeerA); // UI publication deliberately delayed.
        await client.DeleteChatHistoryAsync(PeerA);
        var store = typeof(RemoteClient).GetMethod("StoreChatMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)store.Invoke(client, [Message("new-a", PeerA), null, CancellationToken.None])!;
        foreach (var action in pending.ToArray()) action();
        CollectionAssert.AreEquivalent(AfterDelete, state.ChatMessages.Select(x => x.Id).ToArray());
        CollectionAssert.AreEquivalent(AfterDelete, repo.Messages.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task EmptyDeleteIsRejectedAndUnknownPeerCannotFallBackToAnotherSession()
    {
        var state = new AppState();
        var repo = new MemoryChats();
        await using var client = NewClient(state, repo);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.DeleteChatHistoryAsync(" "));
        Assert.AreEqual(0, repo.DeleteCalls);
        Assert.IsFalse(client.CanChatWithDevice(PeerB));
        Assert.IsNull(await client.SendChatToDeviceAsync(PeerB, "Do not send elsewhere"));
        Assert.AreEqual(0, state.ChatMessages.Count);
    }

    [TestMethod]
    public async Task SendingToSelectedPeerDoesNotUseAnotherActiveSession()
    {
        var state = new AppState();
        var repo = new MemoryChats();
        await using var client = NewClient(state, repo);
        var sessions = (System.Collections.IDictionary)typeof(RemoteClient).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        var a = Session(PeerA); var b = Session(PeerB);
        sessions.Add(a.GetType().GetProperty("Id")!.GetValue(a)!, a);
        sessions.Add(b.GetType().GetProperty("Id")!.GetValue(b)!, b);
        try
        {
            Assert.IsTrue(client.CanChatWithDevice(PeerA));
            Assert.IsTrue(client.CanChatWithDevice(PeerB));
            var message = await client.SendChatToDeviceAsync(PeerB, "For B only");
            Assert.AreEqual(PeerB, message!.PeerDeviceId);
            Assert.AreEqual(PeerB, repo.Messages.Single().PeerDeviceId);
            Assert.IsNull(await client.SendChatToDeviceAsync("333333333", "Must not fall back"));
            Assert.AreEqual(1, repo.Messages.Count);
        }
        finally { sessions.Clear(); ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose(); }
    }

    [TestMethod]
    public void TextExportFiltersPeerAndPreservesUnicodeAndMultilineText()
    {
        var first = Message("first", PeerA, "Hola 👨‍👩‍👧‍👦\nSegunda línea") with { SentUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var second = Message("second", PeerA, "Respuesta 😊") with { IsMine = true };
        var text = ChatTranscript.Format(PeerA, "CASA", [second, Message("private-b", PeerB, "OTHER-DEVICE-ONLY"), first]);
        StringAssert.Contains(text, "Hola 👨‍👩‍👧‍👦\nSegunda línea");
        StringAssert.Contains(text, "Respuesta 😊");
        Assert.IsFalse(text.Contains("OTHER-DEVICE-ONLY", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("Hola", StringComparison.Ordinal) < text.IndexOf("Respuesta", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EmojiCatalogHasHundredsOfValidUnicodeChoicesAndEightCategories()
    {
        Assert.AreEqual(8, EmojiCatalog.Categories.Count);
        Assert.AreEqual(8, EmojiCatalog.Categories.Select(x => x.Key).Distinct().Count());
        var all = EmojiCatalog.Categories.SelectMany(x => x.Emojis).Distinct(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(all.Length > 400);
        var utf8 = new System.Text.UTF8Encoding(false, true);
        foreach (var category in EmojiCatalog.Categories)
        {
            Assert.IsTrue(category.Emojis.Count >= 24);
            Assert.AreEqual(category.Emojis.Count, category.Emojis.Distinct(StringComparer.Ordinal).Count());
            foreach (var emoji in category.Emojis) Assert.IsTrue(utf8.GetByteCount(emoji) > 0);
        }
        CollectionAssert.Contains(all, "👨‍👩‍👧‍👦");
    }

    [TestMethod]
    public async Task TemporaryHostNeverLoadsOrPersistsChatAndDropsDelayedPublicationAfterEnd()
    {
        var pending = new List<Action>();
        var state = new AppState { UiInvoker = pending.Add };
        var repo = new MemoryChats();
        repo.Messages.Add(Message("legacy", PeerA));
        await using var client = NewClient(state, repo, retainHistory: false);
        await client.LoadChatHistoryAsync(PeerA);
        Assert.AreEqual(0, (await client.GetChatHistoryAsync(PeerA)).Count);
        Assert.AreEqual(0, (await client.GetChatPeerDeviceIdsAsync()).Count);
        var sessions = (System.Collections.IDictionary)typeof(RemoteClient).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        var session = Session(PeerA);
        var id = session.GetType().GetProperty("Id")!.GetValue(session)!;
        sessions.Add(id, session);
        try
        {
            await client.SendChatToDeviceAsync(PeerA, "Temporary only");
            Assert.AreEqual(1, repo.Messages.Count); // Only the untouched legacy row, never loaded.
            sessions.Clear();
            var clear = typeof(RemoteClient).GetMethod("ClearTemporaryChatAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)clear.Invoke(client, [PeerA])!;
            foreach (var action in pending.ToArray()) action();
            Assert.AreEqual(0, state.ChatMessages.Count);
            var store = typeof(RemoteClient).GetMethod("StoreChatMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queuedBeforeLateMessage = pending.Count;
            await (Task)store.Invoke(client, [Message("late", PeerA), id, CancellationToken.None])!;
            Assert.AreEqual(queuedBeforeLateMessage, pending.Count);
            Assert.AreEqual(0, state.ChatMessages.Count);
        }
        finally { sessions.Clear(); ((IDisposable)session).Dispose(); }
    }

    [TestMethod]
    public async Task ViewerSessionCleanupDoesNotDeleteHistory()
    {
        var state = new AppState();
        state.ChatMessages.Add(Message("saved", PeerA));
        var repo = new MemoryChats();
        repo.Messages.Add(Message("saved", PeerA));
        await using var client = NewClient(state, repo);
        var clear = typeof(RemoteClient).GetMethod("ClearTemporaryChatAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)clear.Invoke(client, [PeerA])!;
        Assert.AreEqual(1, state.ChatMessages.Count);
        Assert.AreEqual(1, (await client.GetChatHistoryAsync(PeerA)).Count);
    }

    private static RemoteClient NewClient(AppState state, IChatRepository repo, bool retainHistory = true) =>
        new(state, null!, null!, null!, null!, null!, repo, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance,
            new ChatHistoryOptions { RetainHistory = retainHistory });

    private static object Session(string peer)
    {
        var type = typeof(RemoteClient).GetNestedType("RemoteSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(type, nonPublic: true)!;
        var id = Guid.NewGuid();
        void Set(string property, object value) => type.GetProperty(property)!.SetValue(session, value);
        Set("Id", id); Set("RemoteDeviceId", peer); Set("RemoteName", peer);
        Set("Permissions", SessionPermissions.Chat); Set("IsController", false); Set("StartedUtc", DateTimeOffset.UtcNow);
        Set("Handshake", new SessionHandshake());
        Set("Crypto", new SessionCrypto(id, false, new SessionKeys(new byte[32], new byte[32], new byte[32])));
        type.GetMethod("MarkConnected")!.Invoke(session, null);
        return session;
    }

    private sealed class MemoryChats : IChatRepository
    {
        public List<ChatMessage> Messages { get; } = [];
        public int DeleteCalls { get; private set; }
        public Task<IReadOnlyList<string>> GetPeerDeviceIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Messages.Select(x => x.PeerDeviceId).Distinct().ToArray());
        public Task<IReadOnlyList<ChatMessage>> GetThreadAsync(string peerDeviceId, int limit = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(x => x.PeerDeviceId == peerDeviceId).OrderByDescending(x => x.SentUtc).Take(limit).OrderBy(x => x.SentUtc).ToArray());
        public Task AddAsync(ChatMessage message, CancellationToken cancellationToken = default)
        { Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteThreadAsync(string peerDeviceId, CancellationToken cancellationToken = default)
        { DeleteCalls++; Messages.RemoveAll(x => x.PeerDeviceId == peerDeviceId); return Task.CompletedTask; }
    }
}

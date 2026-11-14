using System.Text.Json;
using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private readonly SemaphoreSlim _chatHistoryGate = new(1, 1);
    private readonly bool _retainChatHistory;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _chatHistoryVersions = new(StringComparer.Ordinal);

    public bool CanChatWithDevice(string peerDeviceId) => FindChatSession(peerDeviceId) is not null;

    private RemoteSession? FindChatSession(string peerDeviceId) => _sessions.Values.FirstOrDefault(x =>
        x.RemoteDeviceId == peerDeviceId && x.State == ConnectionState.Connected && x.Permissions.HasFlag(SessionPermissions.Chat));

    public Task<ChatMessage?> SendChatAsync(string text, CancellationToken cancellationToken = default) =>
        SendChatToDeviceAsync(GetPrimarySession()?.RemoteDeviceId ?? "", text, cancellationToken);

    public async Task<ChatMessage?> SendChatToDeviceAsync(string peerDeviceId, string text, CancellationToken cancellationToken = default)
    {
        var session = FindChatSession(peerDeviceId);
        if (session is null)
        {
            return null;
        }

        text = text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > 8000)
        {
            text = text[..(char.IsHighSurrogate(text[7999]) ? 7999 : 8000)];
        }

        var peerId = session.RemoteDeviceId;
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            peerId,
            _state.DeviceId,
            text,
            DateTimeOffset.UtcNow,
            true,
            ChatDeliveryState.Sent);

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new ChatPayload(message.Id, message.Text, message.SentUtc.ToUnixTimeMilliseconds()),
                ProtocolJson.Options);
            if (!await SendEncryptedWithResultAsync(session, SessionFrameKind.Chat, payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false))
                message = message with { DeliveryState = ChatDeliveryState.Failed };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not send chat message");
            message = message with { DeliveryState = ChatDeliveryState.Failed };
        }

        await StoreChatMessageAsync(message, session.Id, cancellationToken).ConfigureAwait(false);
        return message;
    }

    private Task HandleIncomingChatAsync(byte[] payload)
        => HandleIncomingChatCoreAsync(payload, _remoteId ?? "desconocido", _remoteName);

    private Task HandleIncomingChatAsync(byte[] payload, RemoteSession session)
        => HandleIncomingChatCoreAsync(payload, session.RemoteDeviceId, session.RemoteName, session.Id);

    private async Task HandleIncomingChatCoreAsync(byte[] payload, string peerId, string? peerName, Guid? sessionId = null)
    {
        ChatPayload? chat;
        try
        {
            chat = JsonSerializer.Deserialize<ChatPayload>(payload, ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (chat is null || string.IsNullOrWhiteSpace(chat.Text))
        {
            return;
        }

        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var minimumUnixMs = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeMilliseconds();
        if ((!string.IsNullOrEmpty(chat.Id) &&
             (chat.Id.Length > 64 || chat.Id.Any(char.IsControl))) ||
            chat.SentUnixMs < minimumUnixMs ||
            chat.SentUnixMs > nowUnixMs + (long)TimeSpan.FromDays(1).TotalMilliseconds)
        {
            return;
        }

        var message = new ChatMessage(
            string.IsNullOrWhiteSpace(chat.Id) ? Guid.NewGuid().ToString("N") : chat.Id,
            peerId,
            peerId,
            chat.Text.Length > 8000 ? chat.Text[..(char.IsHighSurrogate(chat.Text[7999]) ? 7999 : 8000)] : chat.Text,
            DateTimeOffset.FromUnixTimeMilliseconds(chat.SentUnixMs),
            false,
            ChatDeliveryState.Received);

        await StoreChatMessageAsync(message, sessionId: sessionId).ConfigureAwait(false);

        if (_state.Settings.NotificationsEnabled)
        {
            _notifications.Show(
                peerName ?? "Nuevo mensaje",
                message.Text.Length > 80 ? message.Text[..80] + "…" : message.Text);
        }
    }

    public async Task LoadChatHistoryAsync(string peerDeviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerDeviceId);
        if (!_retainChatHistory) return;
        await _chatHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = await _chatRepository.GetThreadAsync(peerDeviceId, 500, cancellationToken)
                .ConfigureAwait(false);
            var version = _chatHistoryVersions.GetOrAdd(peerDeviceId, 0);
            _state.Post(() =>
            {
                if (_chatHistoryVersions.GetOrAdd(peerDeviceId, 0) != version) return;
                var ids = _state.ChatMessages.Where(x => x.PeerDeviceId == peerDeviceId).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var message in messages)
                {
                    if (message.PeerDeviceId == peerDeviceId && ids.Add(message.Id)) _state.ChatMessages.Add(message);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load chat history");
            throw;
        }
        finally { _chatHistoryGate.Release(); }
    }

    public Task<IReadOnlyList<string>> GetChatPeerDeviceIdsAsync(CancellationToken cancellationToken = default) =>
        _retainChatHistory ? _chatRepository.GetPeerDeviceIdsAsync(cancellationToken) : Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public async Task<IReadOnlyList<ChatMessage>> GetChatHistoryAsync(string peerDeviceId, int limit = int.MaxValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerDeviceId);
        if (!_retainChatHistory) return Array.Empty<ChatMessage>();
        await _chatHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _chatRepository.GetThreadAsync(peerDeviceId, limit, cancellationToken).ConfigureAwait(false); }
        finally { _chatHistoryGate.Release(); }
    }

    public async Task DeleteChatHistoryAsync(string peerDeviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerDeviceId);
        await _chatHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _chatRepository.DeleteThreadAsync(peerDeviceId, cancellationToken).ConfigureAwait(false);
            _chatHistoryVersions.AddOrUpdate(peerDeviceId, 1, (_, version) => version + 1);
            _state.Post(() =>
            {
                for (var i = _state.ChatMessages.Count - 1; i >= 0; i--)
                    if (_state.ChatMessages[i].PeerDeviceId == peerDeviceId) _state.ChatMessages.RemoveAt(i);
            });
        }
        finally { _chatHistoryGate.Release(); }
    }

    private async Task StoreChatMessageAsync(ChatMessage message, Guid? sessionId = null, CancellationToken cancellationToken = default)
    {
        await _chatHistoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_retainChatHistory && (sessionId is not { } id || !_sessions.ContainsKey(id))) return;
            try { if (_retainChatHistory) await _chatRepository.AddAsync(message, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not persist chat message"); }
            var version = _chatHistoryVersions.GetOrAdd(message.PeerDeviceId, 0);
            _state.Post(() =>
            {
                if ((_retainChatHistory || (sessionId is { } activeId && _sessions.ContainsKey(activeId))) &&
                    _chatHistoryVersions.GetOrAdd(message.PeerDeviceId, 0) == version &&
                    !_state.ChatMessages.Any(x => x.PeerDeviceId == message.PeerDeviceId && x.Id == message.Id))
                    _state.ChatMessages.Add(message);
            });
            ChatMessageReceived?.Invoke(this, message);
        }
        finally { _chatHistoryGate.Release(); }
    }

    private async Task ClearTemporaryChatAsync(string peer)
    {
        if (_retainChatHistory) return;
        await _chatHistoryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _chatHistoryVersions.AddOrUpdate(peer, 1, (_, version) => version + 1);
            _state.Post(() =>
            {
                for (var i = _state.ChatMessages.Count - 1; i >= 0; i--)
                    if (_state.ChatMessages[i].PeerDeviceId == peer) _state.ChatMessages.RemoveAt(i);
            });
        }
        finally { _chatHistoryGate.Release(); }
    }
}

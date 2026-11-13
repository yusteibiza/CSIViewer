using System.Text.Json;
using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Protocol;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    public async Task<ChatMessage?> SendChatAsync(string text, CancellationToken cancellationToken = default)
    {
        var session = GetPrimarySession();
        if (session is null || !session.Permissions.HasFlag(SessionPermissions.Chat))
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
            text = text[..8000];
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

        _state.Post(() => _state.ChatMessages.Add(message));
        ChatMessageReceived?.Invoke(this, message);

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new ChatPayload(message.Id, message.Text, message.SentUtc.ToUnixTimeMilliseconds()),
                ProtocolJson.Options);
            if (session.IsController)
            {
                await SendEncryptedAsync(SessionFrameKind.Chat, payload, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendEncryptedAsync(
                        session,
                        SessionFrameKind.Chat,
                        payload,
                        10,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _chatRepository.AddAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not send chat message");
            _state.Post(() =>
            {
                var index = _state.ChatMessages.IndexOf(message);
                if (index >= 0)
                {
                    _state.ChatMessages[index] = message with { DeliveryState = ChatDeliveryState.Failed };
                }
            });
        }

        return message;
    }

    private Task HandleIncomingChatAsync(byte[] payload)
        => HandleIncomingChatCoreAsync(payload, _remoteId ?? "desconocido", _remoteName);

    private Task HandleIncomingChatAsync(byte[] payload, RemoteSession session)
        => HandleIncomingChatCoreAsync(payload, session.RemoteDeviceId, session.RemoteName);

    private async Task HandleIncomingChatCoreAsync(byte[] payload, string peerId, string? peerName)
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
            chat.Text.Length > 8000 ? chat.Text[..8000] : chat.Text,
            DateTimeOffset.FromUnixTimeMilliseconds(chat.SentUnixMs),
            false,
            ChatDeliveryState.Received);

        _state.Post(() => _state.ChatMessages.Add(message));
        ChatMessageReceived?.Invoke(this, message);

        try
        {
            await _chatRepository.AddAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist chat message");
        }

        if (_state.Settings.NotificationsEnabled)
        {
            _notifications.Show(
                peerName ?? "Nuevo mensaje",
                message.Text.Length > 80 ? message.Text[..80] + "…" : message.Text);
        }
    }

    public async Task LoadChatHistoryAsync(string peerDeviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = await _chatRepository.GetThreadAsync(peerDeviceId, 500, cancellationToken)
                .ConfigureAwait(false);
            _state.Post(() =>
            {
                _state.ChatMessages.Clear();
                foreach (var message in messages)
                {
                    _state.ChatMessages.Add(message);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load chat history");
        }
    }
}

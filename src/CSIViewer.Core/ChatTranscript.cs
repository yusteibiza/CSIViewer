using System.Text;

namespace CSIViewer.Core;

public static class ChatTranscript
{
    /// <summary>Plain text export, restricted to the requested conversation.</summary>
    public static string Format(string peerDeviceId, string displayName, IEnumerable<ChatMessage> messages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerDeviceId);
        var text = new StringBuilder();
        text.AppendLine(Localization.Format("Chat con {0}", displayName));
        text.AppendLine("ID: " + peerDeviceId);
        text.AppendLine();
        foreach (var message in messages.Where(x => x.PeerDeviceId == peerDeviceId)
                     .OrderBy(x => x.SentUtc).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var sender = message.IsMine ? Localization.Text("Yo") : displayName;
            text.Append('[').Append(message.SentUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("] ").Append(sender).Append(": ").AppendLine(message.Text);
        }
        return text.ToString();
    }
}

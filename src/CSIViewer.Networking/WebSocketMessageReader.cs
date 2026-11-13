using System.Buffers;
using System.Net.WebSockets;
using CSIViewer.Protocol;

namespace CSIViewer.Networking;

/// <summary>
/// Reads one WebSocket message with a pooled assembly buffer. Small messages
/// take a single allocation; fragmented messages avoid the repeated
/// MemoryStream growth/copy pattern used for video frames and file chunks.
/// </summary>
public static class WebSocketMessageReader
{
    public static async Task<(WebSocketMessageType? Type, byte[] Data)> ReadMessageAsync(
        WebSocket socket,
        byte[] receiveBuffer,
        CancellationToken cancellationToken)
    {
        if (receiveBuffer.Length == 0)
        {
            throw new ArgumentException("El búfer de recepción no puede estar vacío.", nameof(receiveBuffer));
        }

        byte[]? assembly = null;
        var total = 0;
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return (null, []);
                }

                if (total == 0 && result.EndOfMessage)
                {
                    var exact = new byte[result.Count];
                    Buffer.BlockCopy(receiveBuffer, 0, exact, 0, result.Count);
                    return (result.MessageType, exact);
                }

                if (assembly is null)
                {
                    assembly = ArrayPool<byte>.Shared.Rent(
                        Math.Max(receiveBuffer.Length * 2, result.Count * 2));
                }

                if (total + result.Count > assembly.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(
                        Math.Max(assembly.Length * 2, total + result.Count));
                    Buffer.BlockCopy(assembly, 0, larger, 0, total);
                    ArrayPool<byte>.Shared.Return(assembly);
                    assembly = larger;
                }

                Buffer.BlockCopy(receiveBuffer, 0, assembly, total, result.Count);
                total += result.Count;
                if (total > MessageValidation.MaxRelayMessageBytes)
                {
                    throw new InvalidOperationException("Mensaje de red demasiado grande.");
                }

                if (result.EndOfMessage)
                {
                    var exact = new byte[total];
                    Buffer.BlockCopy(assembly, 0, exact, 0, total);
                    return (result.MessageType, exact);
                }
            }
        }
        finally
        {
            if (assembly is not null)
            {
                ArrayPool<byte>.Shared.Return(assembly);
            }
        }
    }
}

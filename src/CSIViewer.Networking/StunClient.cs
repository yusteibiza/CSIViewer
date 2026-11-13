using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CSIViewer.Networking;

public sealed record StunResult(IPEndPoint PublicEndPoint, string Server);

public static class StunClient
{
    private const uint MagicCookie = 0x2112A442;
    private const ushort BindingRequest = 0x0001;
    private const ushort BindingResponse = 0x0101;
    private const ushort AttributeMappedAddress = 0x0001;
    private const ushort AttributeXorMappedAddress = 0x0020;

    public static async Task<StunResult> QueryAsync(
        string host,
        int port = 19302,
        CancellationToken cancellationToken = default)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("No se pudo resolver el servidor STUN.");

        using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveTimeout = 4000;
        var endpoint = new IPEndPoint(address, port);
        var transactionId = RandomNumberGenerator.GetBytes(12);
        var request = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4, 4), MagicCookie);
        transactionId.CopyTo(request.AsSpan(8, 12));

        await socket.SendToAsync(request, SocketFlags.None, endpoint, cancellationToken).ConfigureAwait(false);
        var response = new byte[2048];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var received = await socket.ReceiveFromAsync(response, SocketFlags.None, endpoint, timeout.Token)
            .ConfigureAwait(false);

        if (received.ReceivedBytes < 20)
        {
            throw new InvalidOperationException("Respuesta STUN demasiado corta.");
        }

        var type = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2));
        if (type != BindingResponse)
        {
            throw new InvalidOperationException($"Tipo de respuesta STUN inesperado: 0x{type:X4}.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
        if (length + 20 > received.ReceivedBytes)
        {
            throw new InvalidOperationException("Longitud STUN no válida.");
        }

        var offset = 20;
        var end = 20 + length;
        while (offset + 4 <= end)
        {
            var attributeType = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            var attributeLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 2, 2));
            offset += 4;
            if (offset + attributeLength > end)
            {
                break;
            }

            var value = response.AsSpan(offset, attributeLength);
            if (attributeType is AttributeXorMappedAddress or AttributeMappedAddress && attributeLength >= 8)
            {
                var family = value[1];
                var encodedPort = BinaryPrimitives.ReadUInt16BigEndian(value[2..4]);
                if (family == 0x01)
                {
                    var portValue = attributeType == AttributeXorMappedAddress
                        ? (ushort)(encodedPort ^ (MagicCookie >> 16))
                        : encodedPort;
                    var addressBytes = value.Slice(4, 4).ToArray();
                    if (attributeType == AttributeXorMappedAddress)
                    {
                        var cookie = new byte[4];
                        BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
                        for (var i = 0; i < 4; i++)
                        {
                            addressBytes[i] ^= cookie[i];
                        }
                    }

                    return new StunResult(
                        new IPEndPoint(new IPAddress(addressBytes), portValue),
                        $"{host}:{port}");
                }
            }

            offset += attributeLength;
            offset = (offset + 3) & ~3;
        }

        throw new InvalidOperationException("La respuesta STUN no contiene una dirección pública.");
    }
}

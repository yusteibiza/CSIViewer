using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Server;

public sealed record StunServerOptions(int Port);

/// <summary>
/// Minimal RFC 5389 STUN Binding server. It lets clients discover the public
/// endpoint of their UDP socket. Full ICE/hole punching is still a separate
/// transport feature.
/// </summary>
public sealed class StunServerService : BackgroundService
{
    private const uint MagicCookie = 0x2112A442;
    private const ushort BindingRequest = 0x0001;
    private const ushort BindingSuccess = 0x0101;
    private const ushort AttributeMappedAddress = 0x0001;
    private const ushort AttributeXorMappedAddress = 0x0020;
    private const ushort AttributeSoftware = 0x8022;

    private readonly StunServerOptions _options;
    private readonly ILogger<StunServerService> _logger;

    public StunServerService(StunServerOptions options, ILogger<StunServerService> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, _options.Port));
        _logger.LogInformation("STUN server listening on UDP {Port}", _options.Port);
        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "STUN receive failed");
                continue;
            }

            var response = CreateBindingSuccess(result.Buffer, result.RemoteEndPoint);
            if (response is null)
            {
                continue;
            }

            try
            {
                await udp.SendAsync(response, result.RemoteEndPoint, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "STUN send failed");
            }
        }
    }

    private static byte[]? CreateBindingSuccess(byte[] request, IPEndPoint remote)
    {
        if (request.Length < 20 ||
            BinaryPrimitives.ReadUInt16BigEndian(request) != BindingRequest ||
            BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(4)) != MagicCookie)
        {
            return null;
        }

        var transaction = request.AsSpan(8, 12).ToArray();
        var attributes = new List<byte[]>();
        if (remote.AddressFamily == AddressFamily.InterNetworkV6)
        {
            attributes.Add(CreateXorMappedAddressV6(remote, transaction));
        }
        else
        {
            attributes.Add(CreateXorMappedAddressV4(remote));
            attributes.Add(CreateMappedAddressV4(remote));
        }

        var software = Encoding.ASCII.GetBytes("CSIViewer STUN");
        var softwareAttribute = new byte[4 + software.Length];
        BinaryPrimitives.WriteUInt16BigEndian(softwareAttribute, AttributeSoftware);
        BinaryPrimitives.WriteUInt16BigEndian(softwareAttribute.AsSpan(2), (ushort)software.Length);
        software.CopyTo(softwareAttribute, 4);
        attributes.Add(softwareAttribute);

        var attributeLength = attributes.Sum(x => x.Length);
        var response = new byte[20 + attributeLength];
        BinaryPrimitives.WriteUInt16BigEndian(response, BindingSuccess);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), (ushort)attributeLength);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4), MagicCookie);
        transaction.CopyTo(response, 8);

        var offset = 20;
        foreach (var attribute in attributes)
        {
            attribute.CopyTo(response, offset);
            offset += attribute.Length;
        }

        return response;
    }

    private static byte[] CreateXorMappedAddressV4(IPEndPoint remote)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, AttributeXorMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 8);
        bytes[5] = 0x01; // IPv4
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)(remote.Port ^ 0x2112));
        var address = remote.Address.GetAddressBytes();
        bytes[8] = (byte)(address[0] ^ 0x21);
        bytes[9] = (byte)(address[1] ^ 0x12);
        bytes[10] = (byte)(address[2] ^ 0xA4);
        bytes[11] = (byte)(address[3] ^ 0x42);
        return bytes;
    }

    private static byte[] CreateMappedAddressV4(IPEndPoint remote)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, AttributeMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 8);
        bytes[5] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)remote.Port);
        remote.Address.GetAddressBytes().CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] CreateXorMappedAddressV6(IPEndPoint remote, byte[] transaction)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, AttributeXorMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 20);
        bytes[5] = 0x02; // IPv6
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)(remote.Port ^ 0x2112));
        var address = remote.Address.GetAddressBytes();
        var mask = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
        transaction.CopyTo(mask, 4);
        for (var i = 0; i < 16; i++)
        {
            bytes[8 + i] = (byte)(address[i] ^ mask[i]);
        }

        return bytes;
    }
}

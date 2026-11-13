using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CSIViewer.Server;

public sealed record UpnpMappingResult(bool Success, string Message);

public sealed class UpnpPortMapper : IDisposable
{
    private readonly object _gate = new();
    private bool _mapped;
    private int _mappedExternalPort;
    private string _mappedProtocol = "TCP";

    public bool IsMapped
    {
        get
        {
            lock (_gate)
            {
                return _mapped;
            }
        }
    }

    public Task<UpnpMappingResult> MapAsync(
        int internalPort,
        int externalPort,
        string protocol = "TCP",
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => MapCore(internalPort, externalPort, protocol), cancellationToken);
    }

    public Task<UpnpMappingResult> UnmapAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(UnmapCore, cancellationToken);
    }

    private UpnpMappingResult MapCore(int internalPort, int externalPort, string protocol)
    {
        protocol = string.IsNullOrWhiteSpace(protocol) ? "TCP" : protocol.ToUpperInvariant();
        lock (_gate)
        {
            var localAddress = LocalNetwork.GetPreferredIPv4();
            if (localAddress is null)
            {
                return new UpnpMappingResult(
                    false,
                    "No se encontró una dirección IPv4 local para el servidor.");
            }

            object? nat = null;
            object? mappings = null;
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType is null)
                {
                    return new UpnpMappingResult(
                        false,
                        "Windows no tiene disponible el cliente UPnP (HNetCfg.NATUPnP).");
                }

                nat = Activator.CreateInstance(natType);
                if (nat is null)
                {
                    return new UpnpMappingResult(false, "No se pudo crear el cliente UPnP de Windows.");
                }

                dynamic natObject = nat;
                mappings = natObject.StaticPortMappingCollection;
                if (mappings is null)
                {
                    return new UpnpMappingResult(
                        false,
                        "El router no expone una colección UPnP. Activa UPnP/NAT-PMP en el router.");
                }

                dynamic collection = mappings;
                try
                {
                    collection.Remove(externalPort, protocol);
                }
                catch
                {
                    // There may be no previous mapping.
                }

                collection.Add(
                    externalPort,
                    protocol,
                    internalPort,
                    localAddress,
                    true,
                    "CSIViewer Servidor");
                _mapped = true;
                _mappedExternalPort = externalPort;
                _mappedProtocol = protocol;
                return new UpnpMappingResult(
                    true,
                    $"Puerto abierto en el router: {protocol} {externalPort} → {localAddress}:{internalPort}.");
            }
            catch (Exception ex)
            {
                return new UpnpMappingResult(
                    false,
                    "No se pudo abrir el puerto con UPnP: " + ex.GetBaseException().Message);
            }
            finally
            {
                ReleaseCom(mappings);
                ReleaseCom(nat);
            }
        }
    }

    private UpnpMappingResult UnmapCore()
    {
        lock (_gate)
        {
            if (!_mapped)
            {
                return new UpnpMappingResult(true, "No había ningún puerto UPnP abierto.");
            }

            object? nat = null;
            object? mappings = null;
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType is null)
                {
                    return new UpnpMappingResult(false, "El cliente UPnP de Windows no está disponible.");
                }

                nat = Activator.CreateInstance(natType);
                if (nat is null)
                {
                    return new UpnpMappingResult(false, "No se pudo crear el cliente UPnP de Windows.");
                }

                dynamic natObject = nat;
                mappings = natObject.StaticPortMappingCollection;
                if (mappings is null)
                {
                    return new UpnpMappingResult(false, "El router no expone UPnP.");
                }

                dynamic collection = mappings;
                collection.Remove(_mappedExternalPort, _mappedProtocol);
                _mapped = false;
                return new UpnpMappingResult(
                    true,
                    $"Puerto UPnP {_mappedProtocol} {_mappedExternalPort} cerrado.");
            }
            catch (Exception ex)
            {
                return new UpnpMappingResult(
                    false,
                    "No se pudo cerrar el puerto UPnP: " + ex.GetBaseException().Message);
            }
            finally
            {
                ReleaseCom(mappings);
                ReleaseCom(nat);
            }
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                _ = Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public void Dispose()
    {
        if (IsMapped)
        {
            _ = UnmapCore();
        }
    }
}

public static class LocalNetwork
{
    private static readonly string[] VirtualKeywords =
    [
        "vEthernet", "Hyper-V", "Virtual", "VMware", "VirtualBox",
        "Docker", "WSL", "TAP-", "TUN", "Bluetooth"
    ];

    public static string? GetPreferredIPv4()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                            x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            var physical = interfaces
                .Where(IsPhysical)
                .SelectMany(GetIPv4)
                .ToList();
            if (physical.Count > 0)
            {
                return physical[0];
            }

            return interfaces.SelectMany(GetIPv4).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPhysical(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType is not (
                NetworkInterfaceType.Ethernet or
                NetworkInterfaceType.GigabitEthernet or
                NetworkInterfaceType.FastEthernetFx or
                NetworkInterfaceType.FastEthernetT or
                NetworkInterfaceType.Wireless80211))
        {
            return false;
        }

        var name = networkInterface.Name + " " + networkInterface.Description;
        return !VirtualKeywords.Any(keyword =>
            name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetIPv4(NetworkInterface networkInterface)
    {
        return networkInterface.GetIPProperties().UnicastAddresses
            .Select(x => x.Address)
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(x) &&
                        !x.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(x => x.ToString());
    }
}

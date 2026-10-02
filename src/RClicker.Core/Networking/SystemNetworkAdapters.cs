using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RClicker.Networking;

/// <summary>Reads the machine's real network adapters.</summary>
public static class SystemNetworkAdapters
{
    public static IReadOnlyList<NetworkAdapterInfo> GetAll()
    {
        var result = new List<NetworkAdapterInfo>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        foreach (var nic in interfaces)
        {
            try
            {
                var props = nic.GetIPProperties();
                var addresses = props.UnicastAddresses
                    .Select(u => u.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();
                bool hasGateway = props.GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));

                result.Add(new NetworkAdapterInfo(nic.Name, nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus, hasGateway, addresses));
            }
            catch (NetworkInformationException)
            {
                // One broken adapter should not hide the others.
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return result;
    }

    public static IReadOnlyList<LanAddressCandidate> GetCandidates() => LanAddressSelector.Rank(GetAll());
}

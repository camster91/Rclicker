using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RClicker.Networking;

/// <summary>
/// Picks the IPv4 address most likely to be reachable from a phone on the same Wi-Fi.
/// Pure logic over <see cref="NetworkAdapterInfo"/> snapshots.
/// </summary>
public static class LanAddressSelector
{
    // Name/description fragments of adapters that are usually not the LAN a phone is on.
    private static readonly string[] VirtualAdapterHints =
    [
        "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "wsl", "docker", "vpn",
        "tap-", "tap adapter", "tun", "wireguard", "tailscale", "zerotier", "hamachi", "npcap",
        "loopback", "bluetooth", "openvpn", "anyconnect", "fortinet", "forticlient", "globalprotect",
        "pangp", "nordlynx", "proton", "wintun", "parallels", "utun",
    ];

    /// <summary>
    /// Returns usable candidates, best first. Loopback, link-local (169.254.x.x),
    /// unspecified addresses and adapters that are down are excluded entirely.
    /// </summary>
    public static IReadOnlyList<LanAddressCandidate> Rank(IEnumerable<NetworkAdapterInfo> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var candidates = new List<(LanAddressCandidate Candidate, int Order)>();
        int order = 0;

        foreach (var adapter in adapters)
        {
            if (adapter.Status != OperationalStatus.Up
                || adapter.Type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            bool isVirtual = LooksVirtual(adapter);
            foreach (var address in adapter.IPv4Addresses)
            {
                if (!IsUsableIPv4(address))
                {
                    continue;
                }

                int score = 0;
                if (IsPrivate(address))
                {
                    score += 100;
                }
                else if (IsCarrierGradeNat(address))
                {
                    score += 10; // Often Tailscale or a mobile carrier; rarely the phone's LAN.
                }

                if (adapter.HasIPv4Gateway)
                {
                    score += 50; // Real home/office LANs have a router; Hyper-V/WSL switches usually don't.
                }

                if (adapter.Type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)
                {
                    score += 20;
                }

                if (isVirtual)
                {
                    score -= 200;
                }

                candidates.Add((new LanAddressCandidate(address, adapter.Name, adapter.Description, score, isVirtual), order++));
            }
        }

        return candidates
            .OrderByDescending(c => c.Candidate.Score)
            .ThenBy(c => c.Order)
            .Select(c => c.Candidate)
            .ToList();
    }

    public static LanAddressCandidate? SelectBest(IEnumerable<NetworkAdapterInfo> adapters) => Rank(adapters) is [var best, ..] ? best : null;

    /// <summary>RFC 1918: 10/8, 172.16/12, 192.168/16.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    public static bool IsLinkLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] == 169 && b[1] == 254;
    }

    private static bool IsCarrierGradeNat(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private static bool IsUsableIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address)
            || IsLinkLocal(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        var first = address.GetAddressBytes()[0];
        return first is not 0 and < 224; // Exclude 0.x and multicast/reserved.
    }

    private static bool LooksVirtual(NetworkAdapterInfo adapter)
    {
        foreach (var hint in VirtualAdapterHints)
        {
            if (adapter.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || adapter.Description.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

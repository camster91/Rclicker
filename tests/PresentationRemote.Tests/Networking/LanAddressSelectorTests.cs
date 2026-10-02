using System.Net;
using System.Net.NetworkInformation;
using PresentationRemote.Networking;

namespace PresentationRemote.Tests.Networking;

public class LanAddressSelectorTests
{
    private static NetworkAdapterInfo Adapter(
        string name,
        string ip,
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        bool gateway = true,
        OperationalStatus status = OperationalStatus.Up,
        string? description = null) =>
        new(name, description ?? name, type, status, gateway, [IPAddress.Parse(ip)]);

    [Fact]
    public void TypicalLaptop_PicksWiFiOverHyperVWslAndVpn()
    {
        var adapters = new[]
        {
            Adapter("Loopback Pseudo-Interface 1", "127.0.0.1", NetworkInterfaceType.Loopback, gateway: false),
            Adapter("vEthernet (WSL)", "172.28.112.1", gateway: false, description: "Hyper-V Virtual Ethernet Adapter"),
            Adapter("vEthernet (Default Switch)", "172.17.0.1", gateway: false, description: "Hyper-V Virtual Ethernet Adapter #2"),
            Adapter("WireGuard Tunnel", "10.8.0.2", NetworkInterfaceType.Unknown, gateway: true, description: "WireGuard Tunnel"),
            Adapter("Wi-Fi", "192.168.1.84", NetworkInterfaceType.Wireless80211, description: "Intel(R) Wi-Fi 6 AX201"),
            Adapter("Bluetooth Network Connection", "169.254.10.20", gateway: false, description: "Bluetooth Device (PAN)"),
        };

        var ranked = LanAddressSelector.Rank(adapters);

        Assert.Equal(IPAddress.Parse("192.168.1.84"), ranked[0].Address);
        Assert.False(ranked[0].IsLikelyVirtual);
        Assert.Contains(ranked, c => c.Address.Equals(IPAddress.Parse("10.8.0.2")) && c.IsLikelyVirtual);
        Assert.DoesNotContain(ranked, c => IPAddress.IsLoopback(c.Address));
        Assert.DoesNotContain(ranked, c => c.Address.Equals(IPAddress.Parse("169.254.10.20")));
    }

    [Fact]
    public void EthernetAndWiFiOnSameLan_BothOfferedAsAlternatives()
    {
        var ranked = LanAddressSelector.Rank(
        [
            Adapter("Ethernet", "10.0.0.15"),
            Adapter("Wi-Fi", "10.0.0.22", NetworkInterfaceType.Wireless80211),
        ]);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(IPAddress.Parse("10.0.0.15"), ranked[0].Address); // Equal score: keeps OS order.
    }

    [Fact]
    public void AdaptersThatAreDown_AreIgnored()
    {
        var best = LanAddressSelector.SelectBest(
        [
            Adapter("Ethernet", "192.168.0.10", status: OperationalStatus.Down),
            Adapter("Wi-Fi", "192.168.50.7", NetworkInterfaceType.Wireless80211),
        ]);

        Assert.Equal(IPAddress.Parse("192.168.50.7"), best!.Address);
    }

    [Fact]
    public void NoUsableInterface_ReturnsNothing()
    {
        var adapters = new[]
        {
            Adapter("Loopback", "127.0.0.1", NetworkInterfaceType.Loopback, gateway: false),
            Adapter("Ethernet", "169.254.3.4", gateway: false),
            Adapter("Teredo", "10.1.1.1", NetworkInterfaceType.Tunnel),
        };

        Assert.Empty(LanAddressSelector.Rank(adapters));
        Assert.Null(LanAddressSelector.SelectBest(adapters));
        Assert.Empty(LanAddressSelector.Rank([]));
    }

    [Fact]
    public void PrivateAddress_PreferredOverPublicAndCgnat()
    {
        var ranked = LanAddressSelector.Rank(
        [
            Adapter("Corp", "203.0.113.5"),
            Adapter("Tailscale", "100.101.102.103", NetworkInterfaceType.Unknown, gateway: false, description: "Tailscale Tunnel"),
            Adapter("Wi-Fi", "172.20.10.3", NetworkInterfaceType.Wireless80211), // iPhone hotspot range
        ]);

        Assert.Equal(IPAddress.Parse("172.20.10.3"), ranked[0].Address);
        Assert.Equal(IPAddress.Parse("203.0.113.5"), ranked[1].Address);
        Assert.Equal(3, ranked.Count);
    }

    [Fact]
    public void VirtualAdapterWithGateway_StillLosesToRealLan()
    {
        var ranked = LanAddressSelector.Rank(
        [
            Adapter("VMware Network Adapter VMnet8", "192.168.142.1", description: "VMware Virtual Ethernet Adapter"),
            Adapter("Ethernet 2", "192.168.1.30", description: "Realtek PCIe GbE Family Controller"),
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.30"), ranked[0].Address);
    }

    [Fact]
    public void OnlyVirtualAdapterAvailable_IsStillOffered()
    {
        var best = LanAddressSelector.SelectBest([Adapter("vEthernet (External)", "192.168.1.40", description: "Hyper-V Virtual Ethernet Adapter")]);

        Assert.NotNull(best);
        Assert.True(best.IsLikelyVirtual);
    }

    [Fact]
    public void MultipleAddressesOnOneAdapter_AllConsidered()
    {
        var adapter = new NetworkAdapterInfo("Ethernet", "Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, true,
            [IPAddress.Parse("169.254.1.1"), IPAddress.Parse("192.168.10.5"), IPAddress.IPv6Loopback]);

        var ranked = LanAddressSelector.Rank([adapter]);

        Assert.Single(ranked);
        Assert.Equal(IPAddress.Parse("192.168.10.5"), ranked[0].Address);
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.254", true)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.0.5", false)]
    public void IsPrivate_FollowsRfc1918(string ip, bool expected) =>
        Assert.Equal(expected, LanAddressSelector.IsPrivate(IPAddress.Parse(ip)));

    [Fact]
    public void SystemAdapters_CanBeReadWithoutThrowing()
    {
        // Real machine: just make sure enumeration and ranking never crash.
        var candidates = SystemNetworkAdapters.GetCandidates();
        Assert.All(candidates, c => Assert.False(IPAddress.IsLoopback(c.Address)));
    }
}

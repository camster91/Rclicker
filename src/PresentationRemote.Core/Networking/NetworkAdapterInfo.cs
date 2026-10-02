using System.Net;
using System.Net.NetworkInformation;

namespace PresentationRemote.Networking;

/// <summary>A snapshot of one network adapter. Plain data so selection logic is testable.</summary>
public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    bool HasIPv4Gateway,
    IReadOnlyList<IPAddress> IPv4Addresses);

using System.Net;

namespace RClicker.Networking;

/// <summary>An IPv4 address the phone might be able to reach, with a score (higher is better).</summary>
public sealed record LanAddressCandidate(IPAddress Address, string AdapterName, string AdapterDescription, int Score, bool IsLikelyVirtual)
{
    /// <summary>Text for the desktop adapter picker, e.g. "192.168.1.84 — Wi-Fi".</summary>
    public string DisplayName => IsLikelyVirtual
        ? $"{Address} — {AdapterName} (virtual/VPN)"
        : $"{Address} — {AdapterName}";

    public override string ToString() => DisplayName;
}

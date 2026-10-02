using System.Net;
using System.Net.Sockets;

namespace RClicker.Networking;

/// <summary>Builds the URL that goes into the QR code.</summary>
public static class ControllerUrl
{
    public const string SessionQueryName = "session";

    public static Uri Build(IPAddress address, int port, string token)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 addresses are supported.", nameof(address));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        return new Uri($"http://{address}:{port}/?{SessionQueryName}={Uri.EscapeDataString(token)}");
    }
}

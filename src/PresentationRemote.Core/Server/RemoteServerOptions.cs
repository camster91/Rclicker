using System.Net;
using Microsoft.Extensions.Logging;

namespace PresentationRemote.Server;

public sealed class RemoteServerOptions
{
    public const int DefaultPort = 8765;

    /// <summary>Listen on all IPv4 interfaces so the phone can reach the PC over Wi-Fi or Ethernet.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Any;

    /// <summary>Preferred port. 0 picks a free port (used by tests).</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>If the port is taken, try this many following ports before giving up. 0 disables the fallback.</summary>
    public int PortFallbackAttempts { get; set; } = 10;

    /// <summary>How often the server pings the phone at the WebSocket level.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for the phone's pong before treating the connection as dead.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Hook for adding log providers (console, debug output).</summary>
    public Action<ILoggingBuilder>? ConfigureLogging { get; set; }
}

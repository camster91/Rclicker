namespace RClicker.Relay;

public sealed class RelayClientOptions
{
    public RelayClientOptions(Uri relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        if (!RelayUrls.TryParse(relay.ToString(), out var normalised))
        {
            throw new ArgumentException("The relay must be an https:// address (http:// only for localhost).", nameof(relay));
        }

        Relay = normalised;
    }

    /// <summary>Base address of the relay, e.g. https://rclicker.example.workers.dev/.</summary>
    public Uri Relay { get; }

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>WebSocket ping interval; keeps proxies from dropping an idle connection.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>No pong within this time means the connection is dead; reconnect.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Longest wait between reconnect attempts.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

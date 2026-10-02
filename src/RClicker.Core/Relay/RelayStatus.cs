namespace RClicker.Relay;

public enum RelayState
{
    /// <summary>Connecting to the relay.</summary>
    Connecting,

    /// <summary>The relay can't be reached right now; retrying.</summary>
    Offline,

    /// <summary>Connected to the relay; waiting for a phone to scan the QR code.</summary>
    Ready,

    /// <summary>A phone is connected and in control.</summary>
    PhoneConnected,

    /// <summary>The phone dropped (screen lock, network blip); its seat is held briefly.</summary>
    PhoneReconnecting,

    /// <summary>Stopped (app closing).</summary>
    Stopped,
}

/// <param name="Error">Plain-English reason when <see cref="RelayState.Offline"/>.</param>
public sealed record RelayStatus(RelayState State, string? DeviceLabel = null, string? Error = null)
{
    public static RelayStatus Connecting { get; } = new(RelayState.Connecting);
}

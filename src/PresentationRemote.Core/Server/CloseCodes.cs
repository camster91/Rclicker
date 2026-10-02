namespace PresentationRemote.Server;

/// <summary>
/// Application WebSocket close codes (4000–4999 range). The phone uses these to decide
/// whether to reconnect or to show a final message.
/// </summary>
public static class CloseCodes
{
    /// <summary>The session was regenerated or expired. Do not reconnect; scan the new QR code.</summary>
    public const int SessionEnded = 4401;

    /// <summary>The same browser opened the remote in another tab, which took over.</summary>
    public const int Replaced = 4408;

    /// <summary>Another phone is already the controller.</summary>
    public const int Busy = 4409;

    /// <summary>The receiver app is closing.</summary>
    public const int ReceiverShutdown = 4410;
}

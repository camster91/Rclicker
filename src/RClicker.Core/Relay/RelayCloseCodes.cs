namespace RClicker.Relay;

/// <summary>Close codes the relay uses (see relay/src/protocol.ts).</summary>
public static class RelayCloseCodes
{
    public const int BadRequest = 4400;
    public const int SessionEnded = 4401;
    public const int Forbidden = 4403;
    public const int Replaced = 4408;
    public const int Busy = 4409;
    public const int ReceiverClosed = 4410;
}

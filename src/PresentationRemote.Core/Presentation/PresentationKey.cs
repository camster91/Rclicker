namespace PresentationRemote.Presentation;

/// <summary>The only keys the receiver is ever able to press.</summary>
public enum PresentationKey
{
    RightArrow,
    LeftArrow,
    F5,
    B,
    Escape,
}

/// <summary>Sends a single key press (down + up) to whatever window has keyboard focus.</summary>
public interface IKeySender
{
    /// <summary>Returns false with a short reason if the key could not be sent.</summary>
    bool TrySend(PresentationKey key, out string? error);
}

/// <summary>Information about the window that currently has keyboard focus.</summary>
public sealed record ForegroundWindowInfo(string? ProcessName, string? WindowClass)
{
    public static ForegroundWindowInfo Unknown { get; } = new(null, null);
}

public interface IForegroundWindowProvider
{
    ForegroundWindowInfo GetForegroundWindow();
}

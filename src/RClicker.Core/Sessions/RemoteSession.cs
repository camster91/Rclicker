namespace RClicker.Sessions;

/// <summary>
/// A temporary controller session. Lives only in memory, only while the receiver runs.
/// </summary>
public sealed class RemoteSession
{
    internal RemoteSession(string token, int generation, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Token = token;
        Generation = generation;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The secret. Only goes into the QR code URL; never write it to logs or disk.</summary>
    public string Token { get; }

    /// <summary>Increments every time the session is regenerated. Handy for logs.</summary>
    public int Generation { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string RedactedToken => SessionToken.Redact(Token);

    public override string ToString() => $"session #{Generation} ({RedactedToken})";
}

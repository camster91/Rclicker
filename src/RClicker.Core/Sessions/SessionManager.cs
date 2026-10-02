namespace RClicker.Sessions;

/// <summary>
/// Owns the single current session. Regenerating replaces the key; the PC then ends the old
/// relay room, so old QR codes stop working (old keys are not remembered anywhere).
/// </summary>
public sealed class SessionManager
{
    /// <summary>Long enough for any talk; short enough that a forgotten receiver does not keep a valid token for days.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly Func<string> _tokenFactory;
    private readonly object _gate = new();
    private RemoteSession _current;

    public SessionManager(TimeProvider? time = null, TimeSpan? lifetime = null, Func<string>? tokenFactory = null)
    {
        _time = time ?? TimeProvider.System;
        _lifetime = lifetime ?? DefaultLifetime;
        if (_lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Session lifetime must be positive.");
        }

        _tokenFactory = tokenFactory ?? SessionToken.Generate;
        _current = Create(generation: 1);
    }

    /// <summary>Raised after the session has been replaced. Handlers run on the calling thread.</summary>
    public event EventHandler<RemoteSession>? SessionChanged;

    public RemoteSession Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsExpired(RemoteSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _time.GetUtcNow() >= session.ExpiresAt;
    }

    /// <summary>Creates a new session and invalidates the previous token.</summary>
    public RemoteSession Regenerate()
    {
        RemoteSession next;
        lock (_gate)
        {
            next = Create(_current.Generation + 1);
            _current = next;
        }

        SessionChanged?.Invoke(this, next);
        return next;
    }

    /// <summary>Regenerates only if the current session has expired. Returns true if it did.</summary>
    public bool RegenerateIfExpired()
    {
        if (!IsExpired(Current))
        {
            return false;
        }

        Regenerate();
        return true;
    }

    private RemoteSession Create(int generation)
    {
        var token = _tokenFactory();
        if (!SessionToken.IsWellFormed(token))
        {
            throw new InvalidOperationException("Token factory produced a malformed token.");
        }

        var now = _time.GetUtcNow();
        return new RemoteSession(token, generation, now, now + _lifetime);
    }
}

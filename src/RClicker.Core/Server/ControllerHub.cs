using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RClicker.Sessions;

namespace RClicker.Server;

public enum ControllerState
{
    /// <summary>No phone connected. The QR code is ready to scan.</summary>
    Waiting,

    /// <summary>A phone is connected and in control.</summary>
    Connected,

    /// <summary>The phone dropped (locked screen, Wi-Fi blip). Its seat is held for a short grace period.</summary>
    Reconnecting,
}

public sealed record ControllerStatus(ControllerState State, string? DeviceLabel)
{
    public static ControllerStatus Waiting { get; } = new(ControllerState.Waiting, null);
}

public enum ClaimResult
{
    Claimed,
    Busy,
}

/// <summary>
/// Enforces "one phone in control at a time". The first phone to connect owns the session.
/// Another phone is told the remote is busy instead of silently taking over. The same
/// browser (same client id) may reconnect or take over from its own older tab.
/// </summary>
public sealed class ControllerHub
{
    public static readonly TimeSpan DefaultReconnectGrace = TimeSpan.FromSeconds(30);

    private readonly SessionManager _sessions;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private ControllerConnection? _active;
    private string? _reservedClientId;
    private string? _reservedLabel;
    private ITimer? _graceTimer;
    private ControllerStatus _status = ControllerStatus.Waiting;
    private bool _shuttingDown;

    public ControllerHub(SessionManager sessions, TimeProvider? time = null, TimeSpan? reconnectGrace = null, ILogger<ControllerHub>? logger = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _time = time ?? TimeProvider.System;
        ReconnectGrace = reconnectGrace ?? DefaultReconnectGrace;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _sessions.SessionChanged += OnSessionChanged;
    }

    /// <summary>Raised on any thread when <see cref="Status"/> changes. Read <see cref="Status"/> for the latest value.</summary>
    public event EventHandler<ControllerStatus>? StatusChanged;

    public TimeSpan ReconnectGrace { get; }

    public ControllerStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public bool IsActive(ControllerConnection connection)
    {
        lock (_gate)
        {
            return ReferenceEquals(_active, connection);
        }
    }

    public ClaimResult TryClaim(ControllerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ControllerConnection? replaced = null;
        ControllerStatus status;

        lock (_gate)
        {
            if (_shuttingDown)
            {
                return ClaimResult.Busy;
            }

            if (_active is not null)
            {
                if (!string.Equals(_active.ClientId, connection.ClientId, StringComparison.Ordinal))
                {
                    return ClaimResult.Busy;
                }

                replaced = _active;
            }
            else if (_reservedClientId is not null && !string.Equals(_reservedClientId, connection.ClientId, StringComparison.Ordinal))
            {
                return ClaimResult.Busy;
            }

            _active = connection;
            ClearReservation();
            status = SetStatus(new ControllerStatus(ControllerState.Connected, connection.DeviceLabel));
        }

        _logger.LogInformation("Controller connected ({Device}) on {Session}", connection.DeviceLabel, connection.Session);
        if (replaced is not null)
        {
            _logger.LogInformation("Same device opened the remote again; closing its older connection");
            _ = replaced.CloseAsync(CloseCodes.Replaced, "replaced");
        }

        Raise(status);
        return ClaimResult.Claimed;
    }

    /// <summary>Called when a connection's receive loop ends for any reason.</summary>
    public void Release(ControllerConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ControllerStatus status;

        lock (_gate)
        {
            if (!ReferenceEquals(_active, connection))
            {
                return; // Already replaced, or removed by a session change.
            }

            _active = null;
            bool sessionStillValid = _sessions.Validate(connection.Session.Token) == SessionValidationResult.Valid;
            if (_shuttingDown || !sessionStillValid)
            {
                status = SetStatus(ControllerStatus.Waiting);
            }
            else
            {
                _reservedClientId = connection.ClientId;
                _reservedLabel = connection.DeviceLabel;
                _graceTimer = _time.CreateTimer(_ => OnGraceExpired(connection.ClientId), null, ReconnectGrace, Timeout.InfiniteTimeSpan);
                status = SetStatus(new ControllerStatus(ControllerState.Reconnecting, connection.DeviceLabel));
            }
        }

        _logger.LogInformation("Controller disconnected ({Device}); now {State}", connection.DeviceLabel, status.State);
        Raise(status);
    }

    /// <summary>Disconnects every phone because the receiver is closing.</summary>
    public async Task ShutdownAsync()
    {
        ControllerConnection? active;
        lock (_gate)
        {
            _shuttingDown = true;
            active = _active;
            _active = null;
            ClearReservation();
            SetStatus(ControllerStatus.Waiting);
        }

        _sessions.SessionChanged -= OnSessionChanged;
        if (active is not null)
        {
            await active.CloseAsync(CloseCodes.ReceiverShutdown, "receiver closed");
        }
    }

    private void OnSessionChanged(object? sender, RemoteSession session)
    {
        ControllerConnection? active;
        ControllerStatus status;
        lock (_gate)
        {
            active = _active;
            _active = null;
            ClearReservation();
            status = SetStatus(ControllerStatus.Waiting);
        }

        _logger.LogInformation("Session regenerated ({Session}); previous controller disconnected: {HadController}", session, active is not null);
        if (active is not null)
        {
            _ = active.CloseAsync(CloseCodes.SessionEnded, "session ended");
        }

        Raise(status);
    }

    private void OnGraceExpired(string clientId)
    {
        ControllerStatus status;
        lock (_gate)
        {
            if (_active is not null || !string.Equals(_reservedClientId, clientId, StringComparison.Ordinal))
            {
                return;
            }

            _logger.LogInformation("{Device} did not come back within {Grace}s; seat released", _reservedLabel, ReconnectGrace.TotalSeconds);
            ClearReservation();
            status = SetStatus(ControllerStatus.Waiting);
        }

        Raise(status);
    }

    // Callers hold _gate.
    private void ClearReservation()
    {
        _reservedClientId = null;
        _reservedLabel = null;
        _graceTimer?.Dispose();
        _graceTimer = null;
    }

    // Callers hold _gate.
    private ControllerStatus SetStatus(ControllerStatus status)
    {
        _status = status;
        return status;
    }

    private void Raise(ControllerStatus status)
    {
        try
        {
            StatusChanged?.Invoke(this, status);
        }
#pragma warning disable CA1031 // A UI handler bug must not break the connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "StatusChanged handler failed");
        }
    }
}

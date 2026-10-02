namespace RClicker.Presentation;

/// <summary>
/// Two simple rules so one intentional tap produces exactly one action:
/// 1. The same command repeated within <see cref="DuplicateWindow"/> is a duplicate and is dropped.
/// 2. A small token bucket caps sustained flooding (burst of <see cref="BurstSize"/>, refilling at <see cref="RefillPerSecond"/>/s).
/// Not thread safe; <see cref="CommandRouter"/> serialises calls.
/// </summary>
public sealed class CommandRateLimiter
{
    public static readonly TimeSpan DefaultDuplicateWindow = TimeSpan.FromMilliseconds(200);
    public const int DefaultBurstSize = 6;
    public const double DefaultRefillPerSecond = 3;

    private readonly TimeProvider _time;
    private PresentationCommand? _lastCommand;
    private long _lastTimestamp;
    private double _tokens;
    private long _lastRefillTimestamp;

    public CommandRateLimiter(
        TimeProvider? time = null,
        TimeSpan? duplicateWindow = null,
        int burstSize = DefaultBurstSize,
        double refillPerSecond = DefaultRefillPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(burstSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);
        _time = time ?? TimeProvider.System;
        DuplicateWindow = duplicateWindow ?? DefaultDuplicateWindow;
        BurstSize = burstSize;
        RefillPerSecond = refillPerSecond;
        _tokens = burstSize;
        _lastRefillTimestamp = _time.GetTimestamp();
    }

    public TimeSpan DuplicateWindow { get; }

    public int BurstSize { get; }

    public double RefillPerSecond { get; }

    public RateLimitDecision TryAcquire(PresentationCommand command)
    {
        long now = _time.GetTimestamp();
        Refill(now);

        if (_lastCommand == command && _time.GetElapsedTime(_lastTimestamp, now) < DuplicateWindow)
        {
            return RateLimitDecision.Duplicate;
        }

        if (_tokens < 1)
        {
            return RateLimitDecision.TooFast;
        }

        _tokens -= 1;
        _lastCommand = command;
        _lastTimestamp = now;
        return RateLimitDecision.Allowed;
    }

    private void Refill(long now)
    {
        var elapsed = _time.GetElapsedTime(_lastRefillTimestamp, now);
        _lastRefillTimestamp = now;
        _tokens = Math.Min(BurstSize, _tokens + (elapsed.TotalSeconds * RefillPerSecond));
    }
}

public enum RateLimitDecision
{
    Allowed,
    Duplicate,
    TooFast,
}

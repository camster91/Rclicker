using PresentationRemote.Presentation;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Presentation;

public class CommandRateLimiterTests
{
    [Fact]
    public void DuplicateTapWithinWindow_IsDropped()
    {
        var clock = new ManualTimeProvider();
        var limiter = new CommandRateLimiter(clock);

        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(RateLimitDecision.Duplicate, limiter.TryAcquire(PresentationCommand.Next));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RateLimitDecision.Duplicate, limiter.TryAcquire(PresentationCommand.Next));
    }

    [Fact]
    public void SameCommandAfterWindow_IsAllowed()
    {
        var clock = new ManualTimeProvider();
        var limiter = new CommandRateLimiter(clock);

        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        clock.Advance(CommandRateLimiter.DefaultDuplicateWindow);
        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
    }

    [Fact]
    public void DifferentCommands_AreNotDuplicates()
    {
        var clock = new ManualTimeProvider();
        var limiter = new CommandRateLimiter(clock);

        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Previous));
    }

    [Fact]
    public void Flood_IsCappedByBurst_ThenRefills()
    {
        var clock = new ManualTimeProvider();
        var limiter = new CommandRateLimiter(clock, TimeSpan.Zero, burstSize: 6, refillPerSecond: 3);

        int allowed = 0;
        for (int i = 0; i < 100; i++)
        {
            if (limiter.TryAcquire(PresentationCommand.Next) == RateLimitDecision.Allowed)
            {
                allowed++;
            }
        }

        Assert.Equal(6, allowed);
        Assert.Equal(RateLimitDecision.TooFast, limiter.TryAcquire(PresentationCommand.Next));

        clock.Advance(TimeSpan.FromSeconds(1)); // 3 tokens back.
        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
        Assert.Equal(RateLimitDecision.TooFast, limiter.TryAcquire(PresentationCommand.Next));
    }

    [Fact]
    public void BriskPresenterPace_IsNeverThrottled()
    {
        // Tapping Next about three times a second for 20 seconds (skipping through slides).
        var clock = new ManualTimeProvider();
        var limiter = new CommandRateLimiter(clock);

        for (int i = 0; i < 60; i++)
        {
            Assert.Equal(RateLimitDecision.Allowed, limiter.TryAcquire(PresentationCommand.Next));
            clock.Advance(TimeSpan.FromMilliseconds(340));
        }
    }
}

using RClicker.Sessions;
using RClicker.Tests.TestDoubles;

namespace RClicker.Tests.Sessions;

public class SessionManagerTests
{
    [Fact]
    public void NewManager_HasAWellFormedSession()
    {
        var sessions = new SessionManager();

        Assert.True(SessionToken.IsWellFormed(sessions.Current.Token));
        Assert.Equal(1, sessions.Current.Generation);
    }

    [Fact]
    public void Regenerate_ReplacesTheKey_AndAnnouncesIt()
    {
        var sessions = new SessionManager();
        var old = sessions.Current;
        RemoteSession? announced = null;
        sessions.SessionChanged += (_, s) => announced = s;

        var fresh = sessions.Regenerate();

        Assert.NotEqual(old.Token, fresh.Token);
        Assert.Same(fresh, sessions.Current);
        Assert.Same(fresh, announced);
        Assert.Equal(old.Generation + 1, fresh.Generation);
    }

    [Fact]
    public void ExpiredSession_IsRegenerated()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock, TimeSpan.FromHours(1));
        var first = sessions.Current;

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(sessions.IsExpired(first));
        Assert.False(sessions.RegenerateIfExpired());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(sessions.IsExpired(first));
        Assert.True(sessions.RegenerateIfExpired());
        Assert.NotEqual(first.Token, sessions.Current.Token);
        Assert.False(sessions.IsExpired(sessions.Current));
    }

    [Fact]
    public void Constructor_RejectsMalformedTokenFactory()
    {
        Assert.Throws<InvalidOperationException>(() => new SessionManager(tokenFactory: () => "123456"));
    }

    [Fact]
    public void SessionToString_DoesNotLeakToken()
    {
        var sessions = new SessionManager();

        Assert.DoesNotContain(sessions.Current.Token, sessions.Current.ToString(), StringComparison.Ordinal);
    }
}

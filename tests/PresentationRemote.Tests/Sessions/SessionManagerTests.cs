using PresentationRemote.Sessions;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Sessions;

public class SessionManagerTests
{
    [Fact]
    public void NewManager_HasAValidCurrentSession()
    {
        var sessions = new SessionManager();

        Assert.Equal(SessionValidationResult.Valid, sessions.Validate(sessions.Current.Token));
        Assert.Equal(1, sessions.Current.Generation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsMissingToken(string? token)
    {
        var sessions = new SessionManager();

        Assert.Equal(SessionValidationResult.Missing, sessions.Validate(token));
    }

    [Fact]
    public void Validate_RejectsInvalidTokens()
    {
        var sessions = new SessionManager();
        var real = sessions.Current.Token;

        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate(SessionToken.Generate()));
        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate("not-a-token"));
        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate(real[..^1] + (real[^1] == 'A' ? 'B' : 'A')));
        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate(real + "x"));
    }

    [Fact]
    public void Regenerate_InvalidatesThePreviousToken()
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
        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate(old.Token));
        Assert.Equal(SessionValidationResult.Valid, sessions.Validate(fresh.Token));
    }

    [Fact]
    public void Validate_RejectsExpiredSession_AndRegenerateIfExpiredReplacesIt()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock, TimeSpan.FromHours(1));
        var token = sessions.Current.Token;

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(SessionValidationResult.Valid, sessions.Validate(token));
        Assert.False(sessions.RegenerateIfExpired());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SessionValidationResult.Expired, sessions.Validate(token));

        Assert.True(sessions.RegenerateIfExpired());
        Assert.Equal(SessionValidationResult.Invalid, sessions.Validate(token));
        Assert.Equal(SessionValidationResult.Valid, sessions.Validate(sessions.Current.Token));
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

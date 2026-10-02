using RClicker.Relay;
using RClicker.Sessions;

namespace RClicker.Tests.Relay;

public class RelayUrlsTests
{
    [Theory]
    [InlineData("https://rclicker.example.workers.dev", "https://rclicker.example.workers.dev/")]
    [InlineData("https://relay.example.com/", "https://relay.example.com/")]
    [InlineData("https://example.com/rclicker", "https://example.com/rclicker/")]
    [InlineData("http://localhost:8787", "http://localhost:8787/")]
    [InlineData("http://127.0.0.1:8787/", "http://127.0.0.1:8787/")]
    public void TryParse_AcceptsHttpsAndLocalDevelopment(string input, string expected)
    {
        Assert.True(RelayUrls.TryParse(input, out var relay));
        Assert.Equal(expected, relay.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rclicker.example.workers.dev")]
    [InlineData("http://rclicker.example.workers.dev")] // plain HTTP over the internet
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com/?x=1")]
    [InlineData("https://user:pass@example.com/")]
    public void TryParse_RejectsUnsafeOrInvalid(string? input) => Assert.False(RelayUrls.TryParse(input, out _));

    [Fact]
    public void PhoneUrl_PutsKeyInFragment_SoItNeverReachesTheServer()
    {
        var token = SessionToken.Generate();
        Assert.True(RelayUrls.TryParse("https://rclicker.example.workers.dev", out var relay));

        var url = RelayUrls.PhoneUrl(relay, token);

        Assert.Equal($"https://rclicker.example.workers.dev/#k={token}", url.ToString());
        Assert.Equal(string.Empty, url.Query);
        Assert.Equal("/", url.AbsolutePath);
    }

    [Fact]
    public void HostSocketUrl_UsesSecureWebSocket()
    {
        Assert.True(RelayUrls.TryParse("https://rclicker.example.workers.dev", out var relay));
        Assert.Equal("wss://rclicker.example.workers.dev/ws/host?room=abc", RelayUrls.HostSocketUrl(relay, "abc").ToString());

        Assert.True(RelayUrls.TryParse("http://localhost:8787", out var local));
        Assert.Equal("ws://localhost:8787/ws/host?room=abc", RelayUrls.HostSocketUrl(local, "abc").ToString());
    }
}

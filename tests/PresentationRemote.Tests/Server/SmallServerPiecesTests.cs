using PresentationRemote.Server;

namespace PresentationRemote.Tests.Server;

public class SmallServerPiecesTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15", "iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/126.0 Mobile", "Android")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X)", "iPad")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/126.0", "Windows browser")]
    [InlineData("", "phone")]
    [InlineData(null, "phone")]
    public void DeviceLabel_IsFriendly(string? userAgent, string expected) =>
        Assert.Equal(expected, DeviceLabel.FromUserAgent(userAgent));

    [Theory]
    [InlineData("abcdef12", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("short", false)]
    [InlineData("has space here", false)]
    [InlineData("<script>alert(1)</script>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ClientId_IsSanitised(string? input, bool kept)
    {
        var result = ControllerEndpoint.SanitizeClientId(input);

        if (kept)
        {
            Assert.Equal(input, result);
        }
        else
        {
            Assert.StartsWith("anon-", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClientId_FallbackIsUniquePerConnection() =>
        Assert.NotEqual(ControllerEndpoint.SanitizeClientId(null), ControllerEndpoint.SanitizeClientId(null));

    [Fact]
    public void StaticAssets_OnlyWhitelistedPaths()
    {
        var assets = StaticAssets.LoadEmbedded();

        Assert.Equal(["/", "/app.js", "/index.html", "/styles.css"], assets.Paths.Order(StringComparer.Ordinal));
        Assert.False(assets.TryGet("/../app.js", out _));
        Assert.False(assets.TryGet(null, out _));
    }

    [Fact]
    public void PhonePage_HasNoExternalResources()
    {
        var assets = StaticAssets.LoadEmbedded();
        foreach (var path in assets.Paths)
        {
            Assert.True(assets.TryGet(path, out var asset));
            var text = System.Text.Encoding.UTF8.GetString(asset.Content);
            Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AppVersion_Is010() => Assert.Equal("0.1.0", PresentationRemote.AppInfo.Version);
}

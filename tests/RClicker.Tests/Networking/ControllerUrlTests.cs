using System.Net;
using RClicker.Networking;
using RClicker.Sessions;

namespace RClicker.Tests.Networking;

public class ControllerUrlTests
{
    [Fact]
    public void Build_ProducesTheQrUrlWithSessionToken()
    {
        var token = SessionToken.Generate();

        var url = ControllerUrl.Build(IPAddress.Parse("192.168.1.84"), 8765, token);

        Assert.Equal($"http://192.168.1.84:8765/?session={token}", url.ToString());
        Assert.Equal("192.168.1.84", url.Host);
        Assert.Equal(8765, url.Port);
        Assert.Equal(token, System.Web.HttpUtility.ParseQueryString(url.Query)["session"]);
    }

    [Fact]
    public void Build_EscapesUnexpectedCharacters()
    {
        var url = ControllerUrl.Build(IPAddress.Parse("10.0.0.2"), 9000, "a b&c");

        Assert.Equal("http://10.0.0.2:9000/?session=a%20b%26c", url.AbsoluteUri);
    }

    [Fact]
    public void Build_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => ControllerUrl.Build(IPAddress.IPv6Loopback, 8765, "t"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControllerUrl.Build(IPAddress.Loopback, 0, "t"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ControllerUrl.Build(IPAddress.Loopback, 70000, "t"));
        Assert.Throws<ArgumentException>(() => ControllerUrl.Build(IPAddress.Loopback, 8765, ""));
    }
}

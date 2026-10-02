using System.Net;
using System.Net.Sockets;
using PresentationRemote.Presentation;
using PresentationRemote.Server;
using PresentationRemote.Sessions;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Server;

public class HttpServerTests
{
    [Fact]
    public async Task ControllerPage_IsServed_WithSecurityHeaders()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = new HttpClient { BaseAddress = fixture.BaseUri };

        using var response = await http.GetAsync($"/?session={fixture.Sessions.Current.Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Presentation Remote", html, StringComparison.Ordinal);
        Assert.Contains("data-command=\"presentation.next\"", html, StringComparison.Ordinal);
        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains($"ws://127.0.0.1:{fixture.Server.Port}", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Theory]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/index.html", "text/html")]
    public async Task StaticAssets_AreServed(string path, string mediaType)
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = new HttpClient { BaseAddress = fixture.BaseUri };

        using var response = await http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 100);
    }

    [Theory]
    [InlineData("/../../etc/passwd")]
    [InlineData("/%2e%2e/%2e%2e/etc/passwd")]
    [InlineData("/..%2f..%2fwindows/win.ini")]
    [InlineData("/wwwroot/app.js")]
    [InlineData("/PresentationRemote.Core.dll")]
    [InlineData("/appsettings.json")]
    [InlineData("/APP.JS")]
    [InlineData("/app.js/")]
    public async Task UnknownPaths_AndTraversalAttempts_Return404(string path)
    {
        await using var fixture = await ServerFixture.StartAsync();

        // Raw socket request so the client library does not normalise the path for us.
        var status = await RawGetStatusAsync(fixture.Server.Port, path, $"127.0.0.1:{fixture.Server.Port}");

        Assert.Equal(404, status);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("rebind.attacker.test:8765")]
    public async Task HostNameRequests_AreRefused_ToBlockDnsRebinding(string host)
    {
        await using var fixture = await ServerFixture.StartAsync();

        Assert.Equal(400, await RawGetStatusAsync(fixture.Server.Port, "/", host));
    }

    [Fact]
    public async Task NonGetMethods_AreRejected()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = new HttpClient { BaseAddress = fixture.BaseUri };

        using var response = await http.PostAsync("/ws", new StringContent("{\"type\":\"presentation.next\"}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(fixture.Controller.Calls);
    }

    [Fact]
    public async Task SessionStatusEndpoint_ReportsValidity_FromHeaderOnly()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = new HttpClient { BaseAddress = fixture.BaseUri };

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(http, fixture.Sessions.Current.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, SessionToken.Generate()));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, null));

        var old = fixture.Sessions.Current.Token;
        fixture.Sessions.Regenerate();
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, old));

        // Token in the query string is not accepted by this endpoint.
        using var queryOnly = await http.GetAsync($"/api/session?session={fixture.Sessions.Current.Token}");
        Assert.Equal(HttpStatusCode.Unauthorized, queryOnly.StatusCode);
    }

    [Fact]
    public async Task PlainHttpRequestToWs_WithValidToken_IsBadRequest()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var http = new HttpClient { BaseAddress = fixture.BaseUri };

        using var response = await http.GetAsync($"/ws?session={fixture.Sessions.Current.Token}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PortInUse_FallsBackToNextPort()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        int busyPort = ((IPEndPoint)blocker.LocalEndpoint).Port;

        var sessions = new SessionManager();
        await using var server = new RemoteServer(
            new RemoteServerOptions { BindAddress = IPAddress.Loopback, Port = busyPort, PortFallbackAttempts = 5 },
            sessions,
            new ControllerHub(sessions),
            new CommandRouter(new FakePresentationController()));

        await server.StartAsync();

        Assert.True(server.IsRunning);
        Assert.InRange(server.Port, busyPort + 1, busyPort + 5);
    }

    [Fact]
    public async Task PortInUse_WithoutFallback_GivesFriendlyError()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        int busyPort = ((IPEndPoint)blocker.LocalEndpoint).Port;

        var sessions = new SessionManager();
        await using var server = new RemoteServer(
            new RemoteServerOptions { BindAddress = IPAddress.Loopback, Port = busyPort, PortFallbackAttempts = 0 },
            sessions,
            new ControllerHub(sessions),
            new CommandRouter(new FakePresentationController()));

        var ex = await Assert.ThrowsAsync<ServerStartException>(() => server.StartAsync());

        Assert.Contains(busyPort.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
        Assert.Contains("--port", ex.Message, StringComparison.Ordinal);
        Assert.False(server.IsRunning);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient http, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        if (token is not null)
        {
            request.Headers.Add("X-Session-Token", token);
        }

        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<int> RawGetStatusAsync(int port, string path, string host)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var request = $"GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream);
        var statusLine = await reader.ReadLineAsync() ?? string.Empty;
        return int.Parse(statusLine.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}

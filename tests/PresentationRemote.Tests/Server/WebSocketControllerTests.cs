using System.Net;
using System.Net.WebSockets;
using PresentationRemote.Presentation;
using PresentationRemote.Server;
using PresentationRemote.Sessions;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Server;

/// <summary>End-to-end: real HTTP/WebSocket server on localhost, fake keyboard.</summary>
public class WebSocketControllerTests
{
    [Fact]
    public async Task MissingToken_IsRejectedBeforeUpgrade()
    {
        await using var fixture = await ServerFixture.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await TryConnectStatusAsync(fixture.WebSocketUri(token: null)));
        Assert.Equal(ControllerState.Waiting, fixture.Hub.Status.State);
    }

    [Fact]
    public async Task InvalidToken_IsRejectedBeforeUpgrade()
    {
        await using var fixture = await ServerFixture.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await TryConnectStatusAsync(fixture.WebSocketUri(SessionToken.Generate())));
        Assert.Equal(HttpStatusCode.Unauthorized, await TryConnectStatusAsync(fixture.WebSocketUri("garbage")));
    }

    [Fact]
    public async Task ValidToken_EstablishesControllerSession_AndNextMovesExactlyOneSlide()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();
        await ServerFixture.WaitUntilAsync(() => fixture.Hub.Status.State == ControllerState.Connected);

        await ServerFixture.SendTextAsync(phone, """{"type":"presentation.next","id":1}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(phone);

        Assert.Equal("ack", ack.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, ack.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
        Assert.Equal([PresentationCommand.Next], fixture.Controller.Calls);
    }

    [Fact]
    public async Task AllFiveCommands_ReachTheController()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();
        string[] wire = ["presentation.start", "presentation.next", "presentation.previous", "presentation.black", "presentation.end"];

        foreach (var type in wire)
        {
            await ServerFixture.SendTextAsync(phone, $$"""{"type":"{{type}}"}""");
            using var ack = await ServerFixture.ReceiveJsonAsync(phone);
            Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
        }

        Assert.Equal(
            [PresentationCommand.Start, PresentationCommand.Next, PresentationCommand.Previous, PresentationCommand.ToggleBlack, PresentationCommand.End],
            fixture.Controller.Calls);
    }

    [Fact]
    public async Task UnsupportedAndMalformedMessages_AreRejected_AndConnectionSurvives()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();

        await ServerFixture.SendTextAsync(phone, """{"type":"press-key","key":"F4","id":7}""");
        using (var error = await ServerFixture.ReceiveJsonAsync(phone))
        {
            Assert.Equal("error", error.RootElement.GetProperty("type").GetString());
            Assert.Equal("unsupported_command", error.RootElement.GetProperty("code").GetString());
            Assert.Equal(7, error.RootElement.GetProperty("id").GetInt64());
        }

        await ServerFixture.SendTextAsync(phone, "{this is not json");
        using (var error = await ServerFixture.ReceiveJsonAsync(phone))
        {
            Assert.Equal("malformed", error.RootElement.GetProperty("code").GetString());
        }

        await phone.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, ServerFixture.Timeout());
        using (var error = await ServerFixture.ReceiveJsonAsync(phone))
        {
            Assert.Equal("malformed", error.RootElement.GetProperty("code").GetString());
        }

        await ServerFixture.SendTextAsync(phone, new string('x', 64 * 1024));
        using (var error = await ServerFixture.ReceiveJsonAsync(phone))
        {
            Assert.Equal("too_large", error.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(fixture.Controller.Calls);

        // Still connected and working.
        await ServerFixture.SendTextAsync(phone, """{"type":"presentation.next"}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(phone);
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
        Assert.Equal(WebSocketState.Open, phone.State);
    }

    [Fact]
    public async Task InvalidUtf8_ClosesThatConnection_ServerKeepsWorking()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var phone = await fixture.ConnectAsync(clientId: "presenter-1");

        // RFC 6455 requires failing the connection on invalid UTF-8 text; .NET does this for us.
        await phone.SendAsync(new byte[] { 0xC3, 0x28 }, WebSocketMessageType.Text, true, ServerFixture.Timeout());
        Assert.Equal((int)WebSocketCloseStatus.InvalidPayloadData, await ServerFixture.ReceiveCloseAsync(phone));
        phone.Dispose();
        Assert.Empty(fixture.Controller.Calls);

        using var again = await fixture.ConnectAsync(clientId: "presenter-1");
        await ServerFixture.SendTextAsync(again, """{"type":"presentation.next"}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(again);
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RapidDoubleTap_ProducesOneSlideChange()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();

        await ServerFixture.SendTextAsync(phone, """{"type":"presentation.next","id":1}""");
        await ServerFixture.SendTextAsync(phone, """{"type":"presentation.next","id":2}""");
        using var first = await ServerFixture.ReceiveJsonAsync(phone);
        using var second = await ServerFixture.ReceiveJsonAsync(phone);

        Assert.Equal("ok", first.RootElement.GetProperty("status").GetString());
        Assert.Equal("rate_limited", second.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, fixture.Controller.Count(PresentationCommand.Next));
    }

    [Fact]
    public async Task MessageFlood_DisconnectsTheClient()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();

        for (int i = 0; i < ControllerEndpoint.MaxMessagesPerSecond + 5; i++)
        {
            await ServerFixture.SendTextAsync(phone, """{"type":"ping"}""");
        }

        Assert.Equal((int)WebSocketCloseStatus.PolicyViolation, await ServerFixture.ReceiveCloseAsync(phone));
    }

    [Fact]
    public async Task Ping_GetsPong()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();

        await ServerFixture.SendTextAsync(phone, """{"type":"ping","id":99}""");
        using var pong = await ServerFixture.ReceiveJsonAsync(phone);

        Assert.Equal("pong", pong.RootElement.GetProperty("type").GetString());
        Assert.Equal(99, pong.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task RegeneratingSession_DisconnectsPhone_AndOldTokenStopsWorking()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var oldToken = fixture.Sessions.Current.Token;
        using var phone = await fixture.ConnectAsync();

        fixture.Sessions.Regenerate();

        Assert.Equal(CloseCodes.SessionEnded, await ServerFixture.ReceiveCloseAsync(phone));
        Assert.Equal(HttpStatusCode.Unauthorized, await TryConnectStatusAsync(fixture.WebSocketUri(oldToken)));

        using var again = await fixture.ConnectAsync(fixture.Sessions.Current.Token);
        Assert.Equal(WebSocketState.Open, again.State);
    }

    [Fact]
    public async Task SecondPhone_IsToldBusy_AndCannotControl()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var presenter = await fixture.ConnectAsync(clientId: "presenter-1");

        using var intruder = new ClientWebSocket();
        await intruder.ConnectAsync(fixture.WebSocketUri(fixture.Sessions.Current.Token, "audience-1"), ServerFixture.Timeout());

        Assert.Equal(CloseCodes.Busy, await ServerFixture.ReceiveCloseAsync(intruder));
        Assert.Equal(ControllerState.Connected, fixture.Hub.Status.State);

        await ServerFixture.SendTextAsync(presenter, """{"type":"presentation.next"}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(presenter);
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SameBrowserNewTab_TakesOver_OldTabIsToldReplaced()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var oldTab = await fixture.ConnectAsync(clientId: "presenter-1");
        using var newTab = await fixture.ConnectAsync(clientId: "presenter-1");

        Assert.Equal(CloseCodes.Replaced, await ServerFixture.ReceiveCloseAsync(oldTab));

        await ServerFixture.SendTextAsync(newTab, """{"type":"presentation.next"}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(newTab);
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DroppedPhone_ShowsReconnecting_AndCanReconnect()
    {
        await using var fixture = await ServerFixture.StartAsync(reconnectGrace: TimeSpan.FromSeconds(30));
        var phone = await fixture.ConnectAsync(clientId: "presenter-1");

        phone.Abort(); // Like a phone whose screen locked and Wi-Fi dropped.
        phone.Dispose();
        await ServerFixture.WaitUntilAsync(() => fixture.Hub.Status.State == ControllerState.Reconnecting);

        // Seat is held for the same phone…
        using var other = new ClientWebSocket();
        await other.ConnectAsync(fixture.WebSocketUri(fixture.Sessions.Current.Token, "someone-else"), ServerFixture.Timeout());
        Assert.Equal(CloseCodes.Busy, await ServerFixture.ReceiveCloseAsync(other));

        // …which can come back.
        using var back = await fixture.ConnectAsync(clientId: "presenter-1");
        await ServerFixture.WaitUntilAsync(() => fixture.Hub.Status.State == ControllerState.Connected);
        await ServerFixture.SendTextAsync(back, """{"type":"presentation.previous"}""");
        using var ack = await ServerFixture.ReceiveJsonAsync(back);
        Assert.Equal("ok", ack.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AfterGracePeriod_AnotherPhoneMayConnect()
    {
        await using var fixture = await ServerFixture.StartAsync(reconnectGrace: TimeSpan.FromMilliseconds(200));
        var phone = await fixture.ConnectAsync(clientId: "presenter-1");
        phone.Abort();
        phone.Dispose();

        await ServerFixture.WaitUntilAsync(() => fixture.Hub.Status.State == ControllerState.Waiting);

        using var next = await fixture.ConnectAsync(clientId: "presenter-2");
        Assert.Equal(WebSocketState.Open, next.State);
    }

    [Fact]
    public async Task StoppingServer_TellsPhoneReceiverClosed()
    {
        var fixture = await ServerFixture.StartAsync();
        using var phone = await fixture.ConnectAsync();

        var stop = fixture.Server.StopAsync();

        Assert.Equal(CloseCodes.ReceiverShutdown, await ServerFixture.ReceiveCloseAsync(phone));
        await stop;
        Assert.False(fixture.Server.IsRunning);
    }

    [Fact]
    public async Task CommandsWithExpiredSession_CloseTheConnection()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock, TimeSpan.FromHours(1));
        var controller = new FakePresentationController();
        var hub = new ControllerHub(sessions);
        await using var server = new RemoteServer(
            new RemoteServerOptions { BindAddress = IPAddress.Loopback, Port = 0 },
            sessions,
            hub,
            new CommandRouter(controller));
        await server.StartAsync();

        using var phone = new ClientWebSocket();
        await phone.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/ws?session={sessions.Current.Token}&client=presenter-1"), ServerFixture.Timeout());
        (await ServerFixture.ReceiveJsonAsync(phone)).Dispose(); // hello

        clock.Advance(TimeSpan.FromHours(2));
        await ServerFixture.SendTextAsync(phone, """{"type":"presentation.next"}""");

        Assert.Equal(CloseCodes.SessionEnded, await ServerFixture.ReceiveCloseAsync(phone));
        Assert.Empty(controller.Calls);
    }

    [Fact]
    public async Task CrossOriginWebSocket_IsForbidden()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("Origin", "http://evil.example");

        await Assert.ThrowsAnyAsync<WebSocketException>(() =>
            socket.ConnectAsync(fixture.WebSocketUri(fixture.Sessions.Current.Token), ServerFixture.Timeout()));
        Assert.Equal(HttpStatusCode.Forbidden, socket.HttpStatusCode);
    }

    [Fact]
    public async Task SameOriginWebSocket_IsAllowed()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{fixture.Server.Port}");

        await socket.ConnectAsync(fixture.WebSocketUri(fixture.Sessions.Current.Token), ServerFixture.Timeout());

        Assert.Equal(WebSocketState.Open, socket.State);
    }

    private static async Task<HttpStatusCode> TryConnectStatusAsync(Uri uri)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        await Assert.ThrowsAnyAsync<WebSocketException>(() => socket.ConnectAsync(uri, ServerFixture.Timeout()));
        return socket.HttpStatusCode;
    }
}

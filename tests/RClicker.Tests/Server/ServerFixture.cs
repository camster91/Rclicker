using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RClicker.Presentation;
using RClicker.Server;
using RClicker.Sessions;
using RClicker.Tests.TestDoubles;

namespace RClicker.Tests.Server;

/// <summary>A real Kestrel server on 127.0.0.1 with a free port and a fake presentation controller.</summary>
internal sealed class ServerFixture : IAsyncDisposable
{
    private ServerFixture(SessionManager sessions, ControllerHub hub, FakePresentationController controller, RemoteServer server)
    {
        Sessions = sessions;
        Hub = hub;
        Controller = controller;
        Server = server;
    }

    public SessionManager Sessions { get; }

    public ControllerHub Hub { get; }

    public FakePresentationController Controller { get; }

    public RemoteServer Server { get; }

    public Uri BaseUri => new($"http://127.0.0.1:{Server.Port}/");

    public static async Task<ServerFixture> StartAsync(TimeSpan? reconnectGrace = null, CommandRateLimiter? limiter = null)
    {
        var sessions = new SessionManager();
        var hub = new ControllerHub(sessions, reconnectGrace: reconnectGrace);
        var controller = new FakePresentationController();
        var router = new CommandRouter(controller, limiter ?? new CommandRateLimiter());
        var server = new RemoteServer(
            new RemoteServerOptions { BindAddress = IPAddress.Loopback, Port = 0 },
            sessions,
            hub,
            router);
        await server.StartAsync();
        return new ServerFixture(sessions, hub, controller, server);
    }

    public Uri WebSocketUri(string? token, string? clientId = "test-client-1")
    {
        var query = new List<string>();
        if (token is not null)
        {
            query.Add("session=" + Uri.EscapeDataString(token));
        }

        if (clientId is not null)
        {
            query.Add("client=" + Uri.EscapeDataString(clientId));
        }

        return new Uri($"ws://127.0.0.1:{Server.Port}/ws?{string.Join('&', query)}");
    }

    /// <summary>Connects and consumes the "hello" message.</summary>
    public async Task<ClientWebSocket> ConnectAsync(string? token = null, string clientId = "test-client-1")
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(token ?? Sessions.Current.Token, clientId), Timeout());
        using var hello = await ReceiveJsonAsync(socket);
        Assert.Equal("hello", hello.RootElement.GetProperty("type").GetString());
        return socket;
    }

    public static CancellationToken Timeout(int seconds = 10) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    public static Task SendTextAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Timeout());

    public static async Task<JsonDocument> ReceiveJsonAsync(WebSocket socket)
    {
        var (type, text) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, type);
        return JsonDocument.Parse(text);
    }

    public static async Task<(WebSocketMessageType Type, string Text)> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, Timeout());
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, string.Empty);
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return (result.MessageType, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    /// <summary>Reads until the server closes; returns the close code.</summary>
    public static async Task<int?> ReceiveCloseAsync(WebSocket socket)
    {
        while (true)
        {
            var (type, _) = await ReceiveAsync(socket);
            if (type == WebSocketMessageType.Close)
            {
                return (int?)socket.CloseStatus;
            }
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("Condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}

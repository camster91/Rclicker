using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RClicker.Relay;
using RClicker.Sessions;

namespace RClicker.Tests.Relay;

/// <summary>
/// Minimal stand-in for the Cloudflare relay: accepts /ws/host connections and lets a test
/// script the relay side (send "ready", phone events, encrypted messages) and read what the PC sends.
/// </summary>
internal sealed class FakeRelay : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Channel<HostConnection> _hosts = Channel.CreateUnbounded<HostConnection>();
    private readonly System.Collections.Concurrent.ConcurrentBag<HostConnection> _connections = [];

    private FakeRelay(WebApplication app) => _app = app;

    public Uri Url { get; private set; } = null!;

    public static async Task<FakeRelay> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var relay = new FakeRelay(app);
        app.UseWebSockets();
        app.Run(relay.HandleAsync);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        relay.Url = new Uri(address + "/");
        return relay;
    }

    public async Task<HostConnection> NextHostAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _hosts.Reader.ReadAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            connection.Done.TrySetResult();
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (context.Request.Path != "/ws/host" || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new HostConnection(socket, context.Request.Query["room"].ToString(), context.Request.Headers.UserAgent.ToString());
        _connections.Add(connection);
        connection.StartPump();
        await _hosts.Writer.WriteAsync(connection);
        await connection.Done.Task; // Keep the request alive until the test closes it.
    }

    internal sealed class HostConnection(WebSocket socket, string room, string userAgent)
    {
        public string Room { get; } = room;

        public string UserAgent { get; } = userAgent;

        public WebSocket Socket { get; } = socket;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(object payload) =>
            Socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, true, Timeout());

        private readonly Channel<JsonElement?> _inbox = Channel.CreateUnbounded<JsonElement?>();

        /// <summary>
        /// Reads continuously in the background. (Cancelling a WebSocket receive aborts the
        /// socket in .NET, so tests must never time out a receive directly.)
        /// </summary>
        public void StartPump() => _ = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();
            try
            {
                while (true)
                {
                    var result = await Socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        _inbox.Writer.TryWrite(JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())).RootElement.Clone());
                        stream.SetLength(0);
                    }
                }
            }
            catch (WebSocketException)
            {
            }

            _inbox.Writer.TryWrite(null);
            _inbox.Writer.TryComplete();
        });

        /// <summary>Next JSON message from the PC, or null when it closed.</summary>
        public async Task<JsonElement?> ReceiveAsync(int seconds = 10)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                return await _inbox.Reader.ReadAsync(cts.Token);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        /// <summary>Asserts nothing arrives within the window.</summary>
        public async Task AssertSilentAsync(int milliseconds = 300)
        {
            await Task.Delay(milliseconds);
            if (_inbox.Reader.TryPeek(out var unexpected) && unexpected is not null)
            {
                Assert.Fail("Expected no message but got: " + unexpected.Value.GetRawText());
            }
        }

        public async Task CloseAsync(int code)
        {
            try
            {
                await Socket.CloseOutputAsync((WebSocketCloseStatus)code, "test", Timeout());
            }
            catch (WebSocketException)
            {
            }

            Done.TrySetResult();
        }

        public void Abort()
        {
            Socket.Abort();
            Done.TrySetResult();
        }

        private static CancellationToken Timeout(int seconds = 10) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;
    }
}

/// <summary>Plays the phone's part of the end-to-end protocol through a <see cref="FakeRelay"/>.</summary>
internal sealed class FakePhone(FakeRelay.HostConnection host, string sessionToken, string pid = "phone-pid-000000000001") : IDisposable
{
    private readonly RelayCipher _cipher = new(RelayKeys.Derive(sessionToken));

    public string Pid { get; } = pid;

    public string? Nonce { get; private set; }

    /// <summary>Relay announces the phone; the PC must answer with an encrypted hello.</summary>
    public async Task JoinAsync(string label = "iPhone")
    {
        await host.SendAsync(new { t = "phone", @event = "join", pid = Pid, label });
        var hello = await ReceiveSecureAsync();
        Assert.Equal("hello", hello.GetProperty("type").GetString());
        Nonce = hello.GetProperty("nonce").GetString();
    }

    public async Task SendAsync(object plaintext)
    {
        var (iv, ct) = _cipher.Encrypt(JsonSerializer.Serialize(plaintext), RelayDirection.PhoneToHost);
        await host.SendAsync(new { t = "msg", pid = Pid, iv, ct });
    }

    public async Task<(string Iv, string Ct)> SendCapturedAsync(object plaintext)
    {
        var (iv, ct) = _cipher.Encrypt(JsonSerializer.Serialize(plaintext), RelayDirection.PhoneToHost);
        await host.SendAsync(new { t = "msg", pid = Pid, iv, ct });
        return (iv, ct);
    }

    public Task SendCommandAsync(string type, long id) => SendAsync(new { type, id, nonce = Nonce });

    public async Task<JsonElement> ReceiveSecureAsync()
    {
        var frame = await host.ReceiveAsync() ?? throw new InvalidOperationException("PC closed the connection.");
        Assert.Equal("msg", frame.GetProperty("t").GetString());
        Assert.Equal(Pid, frame.GetProperty("pid").GetString());
        Assert.True(_cipher.TryDecrypt(frame.GetProperty("iv").GetString(), frame.GetProperty("ct").GetString(), RelayDirection.HostToPhone, out var plain));
        return JsonDocument.Parse(plain).RootElement.Clone();
    }

    public void Dispose() => _cipher.Dispose();
}

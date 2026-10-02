using System.Text.Json;
using RClicker.Presentation;
using RClicker.Relay;
using RClicker.Sessions;
using RClicker.Tests.TestDoubles;

namespace RClicker.Tests.Relay;

/// <summary>The PC's relay client against a scripted fake relay. No real keyboard input.</summary>
public sealed class RelayHostClientTests : IAsyncLifetime
{
    private FakeRelay _relay = null!;
    private readonly SessionManager _sessions = new();
    private readonly FakePresentationController _controller = new();
    private RelayHostClient _client = null!;
    private readonly List<RelayState> _states = [];

    public async Task InitializeAsync()
    {
        _relay = await FakeRelay.StartAsync();
        _client = new RelayHostClient(
            new RelayClientOptions(_relay.Url) { MaxBackoff = TimeSpan.FromMilliseconds(200), ConnectTimeout = TimeSpan.FromSeconds(5) },
            _sessions,
            new CommandRouter(_controller));
        _client.StatusChanged += (_, s) =>
        {
            lock (_states)
            {
                _states.Add(s.State);
            }
        };
        _client.Start();
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _relay.DisposeAsync();
    }

    [Fact]
    public async Task Claims_TheRoomDerivedFromTheSession_WithoutRevealingTheKey()
    {
        var host = await _relay.NextHostAsync();
        var keys = RelayKeys.Derive(_sessions.Current.Token);

        var claim = (await host.ReceiveAsync())!.Value;

        Assert.Equal(keys.RoomId, host.Room);
        Assert.Equal("claim", claim.GetProperty("t").GetString());
        Assert.Equal(1, claim.GetProperty("v").GetInt32());
        Assert.Equal(keys.PhoneAuthHash, claim.GetProperty("phoneAuthHash").GetString());
        Assert.True(SessionToken.IsWellFormed(claim.GetProperty("hostKey").GetString()));
        var raw = claim.GetRawText();
        Assert.DoesNotContain(_sessions.Current.Token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(keys.PhoneToken, raw, StringComparison.Ordinal);
        Assert.StartsWith("rclicker/", host.UserAgent, StringComparison.Ordinal);

        await host.SendAsync(new { t = "ready" });
        await WaitForStateAsync(RelayState.Ready);
    }

    [Fact]
    public async Task PhoneCommand_IsDecrypted_Executed_AndAcknowledged()
    {
        var (host, phone) = await ConnectPhoneAsync();
        await WaitForStateAsync(RelayState.PhoneConnected);
        Assert.Equal("iPhone", _client.Status.DeviceLabel);

        await phone.SendCommandAsync("presentation.next", 1);
        var ack = await phone.ReceiveSecureAsync();

        Assert.Equal("ack", ack.GetProperty("type").GetString());
        Assert.Equal(1, ack.GetProperty("id").GetInt64());
        Assert.Equal("ok", ack.GetProperty("status").GetString());
        Assert.Equal([PresentationCommand.Next], _controller.Calls);
        _ = host;
    }

    [Fact]
    public async Task AllFiveCommands_ReachTheController()
    {
        var (_, phone) = await ConnectPhoneAsync();
        string[] wire = ["presentation.start", "presentation.next", "presentation.previous", "presentation.black", "presentation.end"];

        for (int i = 0; i < wire.Length; i++)
        {
            await phone.SendCommandAsync(wire[i], i + 1);
            Assert.Equal("ok", (await phone.ReceiveSecureAsync()).GetProperty("status").GetString());
        }

        Assert.Equal(
            [PresentationCommand.Start, PresentationCommand.Next, PresentationCommand.Previous, PresentationCommand.ToggleBlack, PresentationCommand.End],
            _controller.Calls);
    }

    [Fact]
    public async Task ReplayedCiphertext_IsIgnored()
    {
        var (host, phone) = await ConnectPhoneAsync();
        var (iv, ct) = await phone.SendCapturedAsync(new { type = "presentation.next", id = 1, nonce = phone.Nonce });
        await phone.ReceiveSecureAsync();

        await host.SendAsync(new { t = "msg", pid = phone.Pid, iv, ct }); // the relay (or an attacker) replays it
        await host.AssertSilentAsync();

        await phone.SendCommandAsync("presentation.next", 1); // same id, freshly encrypted
        await host.AssertSilentAsync();
        Assert.Single(_controller.Calls);
    }

    [Fact]
    public async Task CommandWithOldNonce_IsRefused_AndPhoneGetsANewHello()
    {
        var (_, phone) = await ConnectPhoneAsync();

        await phone.SendAsync(new { type = "presentation.next", id = 1, nonce = "an-old-nonce" });
        var ack = await phone.ReceiveSecureAsync();
        var hello = await phone.ReceiveSecureAsync();

        Assert.Equal("rejected", ack.GetProperty("status").GetString());
        Assert.Equal("hello", hello.GetProperty("type").GetString());
        Assert.Empty(_controller.Calls);
    }

    [Fact]
    public async Task UnsupportedAndUnreadableMessages_DoNothing_ConnectionSurvives()
    {
        var (host, phone) = await ConnectPhoneAsync();

        await phone.SendAsync(new { type = "press-key", key = "F4", id = 1, nonce = phone.Nonce });
        var error = await phone.ReceiveSecureAsync();
        Assert.Equal("unsupported_command", error.GetProperty("code").GetString());

        await host.SendAsync(new { t = "msg", pid = phone.Pid, iv = "AAAAAAAAAAAAAAAA", ct = "bm90LWVuY3J5cHRlZC1hdC1hbGwtcmVhbGx5" });
        await host.SendAsync(new { t = "msg", pid = "someone-else-000000000", iv = "x", ct = "y" });
        await host.SendAsync(new { t = "garbage" });
        await host.SendAsync("not even an object");
        await host.AssertSilentAsync();
        Assert.Empty(_controller.Calls);

        await phone.SendCommandAsync("presentation.next", 2);
        Assert.Equal("ok", (await phone.ReceiveSecureAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task RapidDoubleTap_ExecutesOnce()
    {
        var (_, phone) = await ConnectPhoneAsync();

        await phone.SendCommandAsync("presentation.next", 1);
        await phone.SendCommandAsync("presentation.next", 2);
        var first = await phone.ReceiveSecureAsync();
        var second = await phone.ReceiveSecureAsync();

        Assert.Equal("ok", first.GetProperty("status").GetString());
        Assert.Equal("rate_limited", second.GetProperty("status").GetString());
        Assert.Single(_controller.Calls);
    }

    [Fact]
    public async Task PhoneDropping_ShowsReconnecting_ThenReady()
    {
        var (host, phone) = await ConnectPhoneAsync();

        await host.SendAsync(new { t = "phone", @event = "leave", pid = phone.Pid, label = "iPhone", held = true });
        await WaitForStateAsync(RelayState.PhoneReconnecting);

        await host.SendAsync(new { t = "phone", @event = "released", pid = phone.Pid, label = "iPhone" });
        await WaitForStateAsync(RelayState.Ready);
    }

    [Fact]
    public async Task NewSession_EndsOldRoom_AndClaimsANewOne()
    {
        var (host, _) = await ConnectPhoneAsync();
        var oldRoom = host.Room;

        _sessions.Regenerate();

        var end = (await host.ReceiveAsync())!.Value;
        Assert.Equal("end", end.GetProperty("t").GetString());
        Assert.Equal("regenerated", end.GetProperty("reason").GetString());
        await host.CloseAsync(1000);

        var next = await _relay.NextHostAsync();
        Assert.NotEqual(oldRoom, next.Room);
        Assert.Equal(RelayKeys.Derive(_sessions.Current.Token).RoomId, next.Room);
    }

    [Fact]
    public async Task Quit_TellsRelayTheReceiverClosed()
    {
        var (host, _) = await ConnectPhoneAsync();

        var stop = _client.StopAsync();
        var end = (await host.ReceiveAsync())!.Value;
        await host.CloseAsync(1000);
        await stop;

        Assert.Equal("shutdown", end.GetProperty("reason").GetString());
        Assert.Equal(RelayState.Stopped, _client.Status.State);
    }

    [Fact]
    public async Task RelayEndingTheRoom_StartsAFreshSession()
    {
        var host = await _relay.NextHostAsync();
        await host.ReceiveAsync();
        var before = _sessions.Current.Generation;

        await host.CloseAsync(RelayCloseCodes.SessionEnded); // e.g. the room expired

        var next = await _relay.NextHostAsync();
        Assert.Equal(before + 1, _sessions.Current.Generation);
        Assert.Equal(RelayKeys.Derive(_sessions.Current.Token).RoomId, next.Room);
    }

    [Fact]
    public async Task ConnectionDrop_GoesOffline_ThenReclaimsSameRoomWithSameHostKey()
    {
        var host = await _relay.NextHostAsync();
        var claim = (await host.ReceiveAsync())!.Value;
        await host.SendAsync(new { t = "ready" });
        await WaitForStateAsync(RelayState.Ready);

        host.Abort();
        await WaitForStateAsync(RelayState.Offline);

        var again = await _relay.NextHostAsync();
        var reclaim = (await again.ReceiveAsync())!.Value;
        Assert.Equal(host.Room, again.Room);
        Assert.Equal(claim.GetProperty("hostKey").GetString(), reclaim.GetProperty("hostKey").GetString());
        await again.SendAsync(new { t = "ready" });
        await WaitForStateAsync(RelayState.Ready);
    }

    [Fact]
    public async Task RelayUnreachable_ReportsOfflineWithAReason()
    {
        await using var client = new RelayHostClient(
            new RelayClientOptions(new Uri("http://127.0.0.1:9/")) { MaxBackoff = TimeSpan.FromMilliseconds(200) },
            new SessionManager(),
            new CommandRouter(new FakePresentationController()));
        client.Start();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (client.Status.State != RelayState.Offline && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(RelayState.Offline, client.Status.State);
        Assert.False(string.IsNullOrWhiteSpace(client.Status.Error));
    }

    private async Task<(FakeRelay.HostConnection Host, FakePhone Phone)> ConnectPhoneAsync()
    {
        var host = await _relay.NextHostAsync();
        await host.ReceiveAsync(); // claim
        await host.SendAsync(new { t = "ready" });
        var phone = new FakePhone(host, _sessions.Current.Token);
        await phone.JoinAsync();
        return (host, phone);
    }

    private async Task WaitForStateAsync(RelayState state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_states)
            {
                if (_states.Contains(state) && _client.Status.State == state)
                {
                    return;
                }
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Never reached {state}; now {_client.Status.State}. Seen: {string.Join(", ", _states)}");
    }
}

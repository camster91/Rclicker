using System.Net.WebSockets;
using PresentationRemote.Server;
using PresentationRemote.Sessions;
using PresentationRemote.Tests.TestDoubles;

namespace PresentationRemote.Tests.Server;

public class ControllerHubTests
{
    private static ControllerConnection Connection(SessionManager sessions, string clientId) =>
        new(new StubSocket(), sessions.Current, clientId, "iPhone");

    [Fact]
    public void FirstPhone_Claims_SecondPhone_IsBusy()
    {
        var sessions = new SessionManager();
        var hub = new ControllerHub(sessions, new ManualTimeProvider());

        Assert.Equal(ClaimResult.Claimed, hub.TryClaim(Connection(sessions, "phone-aaaa")));
        Assert.Equal(ClaimResult.Busy, hub.TryClaim(Connection(sessions, "phone-bbbb")));
        Assert.Equal(ControllerState.Connected, hub.Status.State);
    }

    [Fact]
    public void SameBrowser_TakesOverFromItsOwnOlderConnection()
    {
        var sessions = new SessionManager();
        var hub = new ControllerHub(sessions, new ManualTimeProvider());
        var first = Connection(sessions, "phone-aaaa");
        var second = Connection(sessions, "phone-aaaa");

        hub.TryClaim(first);

        Assert.Equal(ClaimResult.Claimed, hub.TryClaim(second));
        Assert.True(hub.IsActive(second));
        Assert.False(hub.IsActive(first));
    }

    [Fact]
    public void Disconnect_HoldsSeatForSamePhone_DuringGracePeriod()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock);
        var hub = new ControllerHub(sessions, clock, TimeSpan.FromSeconds(30));
        var states = new List<ControllerState>();
        hub.StatusChanged += (_, s) => states.Add(s.State);
        var phone = Connection(sessions, "phone-aaaa");
        hub.TryClaim(phone);

        hub.Release(phone);
        Assert.Equal(ControllerState.Reconnecting, hub.Status.State);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ClaimResult.Busy, hub.TryClaim(Connection(sessions, "phone-bbbb")));
        Assert.Equal(ClaimResult.Claimed, hub.TryClaim(Connection(sessions, "phone-aaaa")));
        Assert.Equal(ControllerState.Connected, hub.Status.State);
        Assert.Equal([ControllerState.Connected, ControllerState.Reconnecting, ControllerState.Connected], states);
    }

    [Fact]
    public void AfterGracePeriod_SeatIsFreeForAnotherPhone()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock);
        var hub = new ControllerHub(sessions, clock, TimeSpan.FromSeconds(30));
        var phone = Connection(sessions, "phone-aaaa");
        hub.TryClaim(phone);
        hub.Release(phone);

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(ControllerState.Waiting, hub.Status.State);
        Assert.Equal(ClaimResult.Claimed, hub.TryClaim(Connection(sessions, "phone-bbbb")));
    }

    [Fact]
    public void RegeneratingSession_DisconnectsControllerAndFreesSeat()
    {
        var clock = new ManualTimeProvider();
        var sessions = new SessionManager(clock);
        var hub = new ControllerHub(sessions, clock);
        var phone = Connection(sessions, "phone-aaaa");
        hub.TryClaim(phone);

        sessions.Regenerate();

        Assert.False(hub.IsActive(phone));
        Assert.Equal(ControllerState.Waiting, hub.Status.State);
        Assert.Equal(CloseCodes.SessionEnded, (int?)((StubSocket)phone.Socket).ClosedWith);

        // Releasing the old connection later must not put the hub into "reconnecting".
        hub.Release(phone);
        Assert.Equal(ControllerState.Waiting, hub.Status.State);
    }

    [Fact]
    public async Task Shutdown_ClosesWithShutdownCode_AndRefusesNewPhones()
    {
        var sessions = new SessionManager();
        var hub = new ControllerHub(sessions, new ManualTimeProvider());
        var phone = Connection(sessions, "phone-aaaa");
        hub.TryClaim(phone);

        await hub.ShutdownAsync();

        Assert.Equal(CloseCodes.ReceiverShutdown, (int?)((StubSocket)phone.Socket).ClosedWith);
        Assert.Equal(ClaimResult.Busy, hub.TryClaim(Connection(sessions, "phone-aaaa")));
    }

    /// <summary>Minimal in-memory socket: records the close code and nothing else.</summary>
    private sealed class StubSocket : WebSocket
    {
        public WebSocketCloseStatus? ClosedWith { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => ClosedWith;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => ClosedWith is null ? WebSocketState.Open : WebSocketState.CloseSent;

        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedWith = closeStatus;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromCanceled<WebSocketReceiveResult>(new CancellationToken(true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

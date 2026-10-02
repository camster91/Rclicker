using System.Net.WebSockets;
using System.Text;
using PresentationRemote.Sessions;

namespace PresentationRemote.Server;

/// <summary>One phone's WebSocket. Sends are serialised; closing is best effort and never throws.</summary>
public sealed class ControllerConnection : IDisposable
{
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(2);

    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _abort = new();
    private int _closing;

    public ControllerConnection(WebSocket socket, RemoteSession session, string clientId, string deviceLabel)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        ClientId = clientId;
        DeviceLabel = deviceLabel;
    }

    /// <summary>The session this connection authenticated with. Re-checked on every command.</summary>
    public RemoteSession Session { get; }

    /// <summary>Random per-browser id (not a secret). Lets the same phone reconnect without being "another phone".</summary>
    public string ClientId { get; }

    public string DeviceLabel { get; }

    public WebSocket Socket => _socket;

    /// <summary>Cancelled when the connection is being torn down; the receive loop watches it.</summary>
    public CancellationToken Aborted => _abort.Token;

    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The phone went away mid-send. The receive loop will notice and clean up.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a close frame with an application close code, then forcibly aborts the
    /// connection if the phone does not answer quickly (e.g. it is asleep).
    /// </summary>
    public async Task CloseAsync(int code, string reason)
    {
        if (Interlocked.Exchange(ref _closing, 1) == 1)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(CloseHandshakeTimeout);
            await _sendLock.WaitAsync(timeout.Token);
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.CloseOutputAsync((WebSocketCloseStatus)code, reason, timeout.Token);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }

        // Give the receive loop a moment to see the phone's close reply, then pull the plug.
        try
        {
            _abort.CancelAfter(CloseHandshakeTimeout);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _abort.Dispose();
        _sendLock.Dispose();
    }
}

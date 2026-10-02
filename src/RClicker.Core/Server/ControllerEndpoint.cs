using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RClicker.Presentation;
using RClicker.Sessions;

namespace RClicker.Server;

/// <summary>
/// Handles <c>GET /ws?session=TOKEN&amp;client=ID</c>: authenticates before the WebSocket
/// upgrade, then runs the receive loop for one phone.
/// </summary>
internal sealed class ControllerEndpoint
{
    /// <summary>Above this many messages per second the connection is dropped (flood protection).</summary>
    internal const int MaxMessagesPerSecond = 20;

    private readonly SessionManager _sessions;
    private readonly ControllerHub _hub;
    private readonly CommandRouter _router;
    private readonly RemoteServerOptions _options;
    private readonly string _version;
    private readonly ILogger _logger;

    public ControllerEndpoint(SessionManager sessions, ControllerHub hub, CommandRouter router, RemoteServerOptions options, string version, ILogger logger)
    {
        _sessions = sessions;
        _hub = hub;
        _router = router;
        _options = options;
        _version = version;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        var token = context.Request.Query[Networking.ControllerUrl.SessionQueryName].ToString();
        var validation = _sessions.Validate(token);
        if (validation != SessionValidationResult.Valid)
        {
            _logger.LogInformation("Rejected controller connection from {Remote}: {Reason} token {Token}", context.Connection.RemoteIpAddress, validation, SessionToken.Redact(token));
            await HttpResults.SessionStatusAsync(context, validation);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var session = _sessions.Current;
        var clientId = SanitizeClientId(context.Request.Query["client"].ToString());
        var device = DeviceLabel.FromUserAgent(context.Request.Headers.UserAgent.ToString());

        using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
        {
            KeepAliveInterval = _options.KeepAliveInterval,
            KeepAliveTimeout = _options.KeepAliveTimeout,
        });
        using var connection = new ControllerConnection(socket, session, clientId, device);

        if (_hub.TryClaim(connection) == ClaimResult.Busy)
        {
            _logger.LogInformation("Refused a second phone ({Device}) from {Remote}: a controller is already active", device, context.Connection.RemoteIpAddress);
            await connection.CloseAsync(CloseCodes.Busy, "busy");
            await DrainUntilClosedAsync(connection, context.RequestAborted);
            return;
        }

        try
        {
            await connection.SendAsync(ControllerProtocol.Hello(_version), context.RequestAborted);
            await ReceiveLoopAsync(connection, context.RequestAborted);
        }
        finally
        {
            _hub.Release(connection);
        }
    }

    internal static string SanitizeClientId(string? candidate)
    {
        if (!string.IsNullOrEmpty(candidate) && candidate.Length is >= 8 and <= 64
            && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return candidate;
        }

        // No usable id: treat this connection as its own unique device.
        return "anon-" + Guid.NewGuid().ToString("N");
    }

    private async Task ReceiveLoopAsync(ControllerConnection connection, CancellationToken requestAborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, connection.Aborted);
        var ct = linked.Token;
        var socket = connection.Socket;
        var buffer = ArrayPool<byte>.Shared.Rent(ControllerProtocol.MaxMessageBytes + 1);
        long windowStart = Environment.TickCount64;
        int messagesInWindow = 0;

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var (kind, length, tooLarge) = await ReadMessageAsync(socket, buffer, ct);
                if (kind == WebSocketMessageType.Close)
                {
                    break;
                }

                long now = Environment.TickCount64;
                if (now - windowStart >= 1000)
                {
                    windowStart = now;
                    messagesInWindow = 0;
                }

                if (++messagesInWindow > MaxMessagesPerSecond)
                {
                    _logger.LogWarning("Controller is flooding messages; disconnecting");
                    await connection.CloseAsync((int)WebSocketCloseStatus.PolicyViolation, "too many messages");
                    continue; // Wait for the close reply (or the abort).
                }

                if (tooLarge)
                {
                    await connection.SendAsync(ControllerProtocol.Error(ProtocolErrors.TooLarge, null), ct);
                    continue;
                }

                if (kind != WebSocketMessageType.Text)
                {
                    await connection.SendAsync(ControllerProtocol.Error(ProtocolErrors.Malformed, null), ct);
                    continue;
                }

                string text;
                try
                {
                    text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, length);
                }
                catch (DecoderFallbackException)
                {
                    await connection.SendAsync(ControllerProtocol.Error(ProtocolErrors.Malformed, null), ct);
                    continue;
                }

                if (!await HandleMessageAsync(connection, text, ct))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            _logger.LogDebug("Controller connection ended: {Message}", ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Returns false when the connection should end.</summary>
    private async Task<bool> HandleMessageAsync(ControllerConnection connection, string text, CancellationToken ct)
    {
        var message = ControllerProtocol.Parse(text);
        switch (message.Kind)
        {
            case ClientMessageKind.Ping:
                await connection.SendAsync(ControllerProtocol.Pong(message.Id), ct);
                return true;

            case ClientMessageKind.Error:
                _logger.LogInformation("Ignored bad message from controller: {Code}", message.ErrorCode);
                await connection.SendAsync(ControllerProtocol.Error(message.ErrorCode!, message.Id), ct);
                return true;
        }

        // Re-check on every command: the session may have expired or been regenerated,
        // and this connection may have been replaced by a newer tab.
        if (_sessions.Validate(connection.Session.Token) != SessionValidationResult.Valid)
        {
            await connection.CloseAsync(CloseCodes.SessionEnded, "session ended");
            return false;
        }

        if (!_hub.IsActive(connection))
        {
            return false;
        }

        var result = _router.Execute(message.Command);
        await connection.SendAsync(ControllerProtocol.Ack(message.Id, message.Command, result), ct);
        return true;
    }

    /// <summary>Reads one whole message. Oversized messages are consumed and flagged, never buffered.</summary>
    private static async Task<(WebSocketMessageType Kind, int Length, bool TooLarge)> ReadMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        int length = 0;
        bool tooLarge = false;
        while (true)
        {
            var segment = tooLarge || length >= ControllerProtocol.MaxMessageBytes
                ? new ArraySegment<byte>(buffer, 0, buffer.Length)
                : new ArraySegment<byte>(buffer, length, buffer.Length - length);
            var result = await socket.ReceiveAsync(segment, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, 0, false);
            }

            if (!tooLarge)
            {
                length += result.Count;
                if (length > ControllerProtocol.MaxMessageBytes)
                {
                    tooLarge = true;
                }
            }

            if (result.EndOfMessage)
            {
                return (result.MessageType, tooLarge ? 0 : length, tooLarge);
            }
        }
    }

    private static async Task DrainUntilClosedAsync(ControllerConnection connection, CancellationToken requestAborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, connection.Aborted);
        var buffer = new byte[256];
        try
        {
            while (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await connection.Socket.ReceiveAsync(buffer, linked.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
        }
    }
}

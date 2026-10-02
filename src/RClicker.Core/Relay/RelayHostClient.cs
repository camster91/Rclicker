using System.Buffers.Text;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RClicker.Presentation;
using RClicker.Protocol;
using RClicker.Sessions;

namespace RClicker.Relay;

/// <summary>
/// The PC side of the cloud relay. Makes one outbound WebSocket (HTTPS port 443) to the
/// relay, so it works where phone and PC cannot reach each other directly. Decrypts phone
/// commands, checks they are fresh, runs them, and sends encrypted acknowledgements back.
/// </summary>
public sealed class RelayHostClient : IAsyncDisposable
{
    private const int MaxRelayMessageChars = 4096;

    private readonly RelayClientOptions _options;
    private readonly SessionManager _sessions;
    private readonly CommandRouter _router;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _hostKeys = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private Task? _loop;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCts;
    private RelayStatus _status = RelayStatus.Connecting;

    public RelayHostClient(RelayClientOptions options, SessionManager sessions, CommandRouter router, ILogger<RelayHostClient>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _sessions.SessionChanged += OnSessionChanged;
    }

    /// <summary>Raised on a background thread. Read <see cref="Status"/> for the latest value.</summary>
    public event EventHandler<RelayStatus>? StatusChanged;

    public RelayStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public Uri Relay => _options.Relay;

    /// <summary>URL for the QR code of the current session.</summary>
    public Uri PhoneUrl => RelayUrls.PhoneUrl(_options.Relay, _sessions.Current.Token);

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("Already started.");
        }

        _loop = Task.Run(RunAsync);
    }

    /// <summary>Tells the relay the receiver is closing (phone shows "rclicker closed"), then disconnects.</summary>
    public async Task StopAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await SendEndAsync("shutdown");
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Relay loop did not stop in time");
            }
        }

        SetStatus(new RelayStatus(RelayState.Stopped));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _sessions.SessionChanged -= OnSessionChanged;
        _stop.Dispose();
        _sendLock.Dispose();
    }

    private async Task RunAsync()
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            var session = _sessions.Current;
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            lock (_gate)
            {
                _sessionCts = sessionCts;
            }

            if (session != _sessions.Current)
            {
                continue; // Regenerated while we were setting up.
            }

            try
            {
                if (failures == 0)
                {
                    SetStatus(RelayStatus.Connecting);
                }

                await RunSessionAsync(session, onReady: () => failures = 0, sessionCts.Token);
                if (session != _sessions.Current || _stop.IsCancellationRequested)
                {
                    continue; // Normal: new session or shutting down.
                }

                failures++;
                SetStatus(new RelayStatus(RelayState.Offline, Error: "The relay closed the connection. Reconnecting…"));
            }
            catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
            {
                continue; // Session regenerated or app stopping.
            }
#pragma warning disable CA1031 // Any failure means "offline, retry"; never crash the app.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures++;
                _logger.LogWarning("Relay connection failed ({Type}): {Message}", ex.GetType().Name, ex.Message);
                SetStatus(new RelayStatus(RelayState.Offline, Error: Explain(ex)));
            }

            var delay = TimeSpan.FromSeconds(Math.Min(_options.MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(failures, 6) - 1)));
            try
            {
                await Task.Delay(delay, sessionCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Stopping, or a new session: loop around immediately.
            }
        }
    }

    private async Task RunSessionAsync(RemoteSession session, Action onReady, CancellationToken ct)
    {
        var keys = RelayKeys.Derive(session.Token);
        using var cipher = new RelayCipher(keys);
        using var socket = CreateSocket();

        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(_options.ConnectTimeout);
            await socket.ConnectAsync(RelayUrls.HostSocketUrl(_options.Relay, keys.RoomId), connectTimeout.Token);
        }

        lock (_gate)
        {
            _socket = socket;
        }

        try
        {
            await SendJsonAsync(socket, new { t = "claim", v = 1, hostKey = HostKeyFor(session), phoneAuthHash = keys.PhoneAuthHash }, ct);
            var phones = new Dictionary<string, PhoneChannel>(StringComparer.Ordinal);
            bool ready = false;

            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readyTimeout.CancelAfter(_options.ConnectTimeout);

            while (socket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextAsync(socket, ready ? ct : readyTimeout.Token);
                if (text is null)
                {
                    break;
                }

                await HandleRelayMessageAsync(socket, cipher, phones, text, () =>
                {
                    ready = true;
                    onReady();
                }, ct);
            }

            HandleRelayClose(socket, session);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                }
            }
        }
    }

    private void HandleRelayClose(ClientWebSocket socket, RemoteSession session)
    {
        var code = (int?)socket.CloseStatus;
        _logger.LogInformation("Relay closed the connection: {Code} {Reason}", code, socket.CloseStatusDescription);

        // The room is over (expired, or this key is no longer valid). Start a fresh session.
        if ((code == RelayCloseCodes.SessionEnded || code == RelayCloseCodes.Forbidden) && session == _sessions.Current && !_stop.IsCancellationRequested)
        {
            _logger.LogInformation("Room ended by the relay; creating a new session");
            _sessions.Regenerate();
        }
    }

    private async Task HandleRelayMessageAsync(
        ClientWebSocket socket,
        RelayCipher cipher,
        Dictionary<string, PhoneChannel> phones,
        string text,
        Action onReady,
        CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String)
            {
                return;
            }

            switch (t.GetString())
            {
                case "ready":
                    _logger.LogInformation("Relay room ready ({Session})", _sessions.Current);
                    onReady();
                    if (phones.Count == 0)
                    {
                        SetStatus(new RelayStatus(RelayState.Ready));
                    }

                    break;

                case "phone":
                    await HandlePhoneEventAsync(socket, cipher, phones, root, ct);
                    break;

                case "msg":
                    await HandlePhoneMessageAsync(socket, cipher, phones, root, ct);
                    break;
            }
        }
    }

    private async Task HandlePhoneEventAsync(ClientWebSocket socket, RelayCipher cipher, Dictionary<string, PhoneChannel> phones, JsonElement root, CancellationToken ct)
    {
        var pid = GetString(root, "pid");
        var label = GetString(root, "label") ?? "phone";
        if (pid is null)
        {
            return;
        }

        switch (GetString(root, "event"))
        {
            case "join":
                var channel = new PhoneChannel(pid, label, Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
                phones.Clear(); // The relay allows one phone; a join replaces any earlier one.
                phones[pid] = channel;
                _logger.LogInformation("Phone connected ({Device})", label);
                SetStatus(new RelayStatus(RelayState.PhoneConnected, label));
                await SendEncryptedAsync(socket, cipher, pid, Hello(channel.Nonce), ct);
                break;

            case "leave":
                phones.Remove(pid);
                bool held = root.TryGetProperty("held", out var h) && h.ValueKind == JsonValueKind.True;
                _logger.LogInformation("Phone disconnected ({Device}); seat held: {Held}", label, held);
                if (phones.Count == 0)
                {
                    SetStatus(held ? new RelayStatus(RelayState.PhoneReconnecting, label) : new RelayStatus(RelayState.Ready));
                }

                break;

            case "released":
                if (phones.Count == 0)
                {
                    SetStatus(new RelayStatus(RelayState.Ready));
                }

                break;
        }
    }

    private async Task HandlePhoneMessageAsync(ClientWebSocket socket, RelayCipher cipher, Dictionary<string, PhoneChannel> phones, JsonElement root, CancellationToken ct)
    {
        var pid = GetString(root, "pid");
        if (pid is null || !phones.TryGetValue(pid, out var channel))
        {
            return;
        }

        if (!cipher.TryDecrypt(GetString(root, "iv"), GetString(root, "ct"), RelayDirection.PhoneToHost, out var plaintext))
        {
            _logger.LogWarning("Dropped a message that failed decryption");
            return;
        }

        var freshness = CheckFreshness(plaintext, channel);
        if (freshness == Freshness.Replayed)
        {
            _logger.LogWarning("Dropped a replayed command");
            return;
        }

        var message = ControllerProtocol.Parse(plaintext);
        if (freshness == Freshness.StaleNonce && message.Kind == ClientMessageKind.Command)
        {
            // Phone sent this before our latest hello (e.g. the PC just reconnected).
            await SendEncryptedAsync(socket, cipher, pid, ControllerProtocol.Ack(message.Id, message.Command, CommandResult.Rejected("Connection refreshed. Tap again.")), ct);
            await SendEncryptedAsync(socket, cipher, pid, Hello(channel.Nonce), ct);
            return;
        }

        switch (message.Kind)
        {
            case ClientMessageKind.Ping:
                await SendEncryptedAsync(socket, cipher, pid, ControllerProtocol.Pong(message.Id), ct);
                break;

            case ClientMessageKind.Error:
                await SendEncryptedAsync(socket, cipher, pid, ControllerProtocol.Error(message.ErrorCode!, message.Id), ct);
                break;

            case ClientMessageKind.Command when freshness == Freshness.Fresh:
                channel.LastId = message.Id!.Value;
                var result = _router.Execute(message.Command);
                await SendEncryptedAsync(socket, cipher, pid, ControllerProtocol.Ack(message.Id, message.Command, result), ct);
                break;

            case ClientMessageKind.Command:
                await SendEncryptedAsync(socket, cipher, pid, ControllerProtocol.Error(ProtocolErrors.Malformed, message.Id), ct);
                break;
        }
    }

    /// <summary>
    /// Commands must carry the nonce from our latest hello and a strictly increasing id,
    /// so a recorded message can never be played back.
    /// </summary>
    private static Freshness CheckFreshness(string plaintext, PhoneChannel channel)
    {
        try
        {
            using var doc = JsonDocument.Parse(plaintext);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Freshness.Invalid;
            }

            if (!root.TryGetProperty("nonce", out var nonce) || nonce.ValueKind != JsonValueKind.String
                || !string.Equals(nonce.GetString(), channel.Nonce, StringComparison.Ordinal))
            {
                return Freshness.StaleNonce;
            }

            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value))
            {
                return Freshness.Invalid;
            }

            return value > channel.LastId ? Freshness.Fresh : Freshness.Replayed;
        }
        catch (JsonException)
        {
            return Freshness.Invalid;
        }
    }

    private static string Hello(string nonce)
    {
        using var doc = JsonDocument.Parse(ControllerProtocol.Hello(AppInfo.Version));
        var fields = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        fields["nonce"] = nonce;
        return JsonSerializer.Serialize(fields);
    }

    private void OnSessionChanged(object? sender, RemoteSession session)
    {
        // End the old room (its phone sees "Session ended"), then let the loop start the new one.
        _ = Task.Run(async () =>
        {
            await SendEndAsync("regenerated");
            CancellationTokenSource? cts;
            lock (_gate)
            {
                cts = _sessionCts;
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
    }

    private async Task SendEndAsync(string reason)
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
        }

        if (socket is null || socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SendJsonAsync(socket, new { t = "end", reason }, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("Could not tell the relay the session ended: {Message}", ex.Message);
        }
    }

    private ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = _options.KeepAliveInterval;
        socket.Options.KeepAliveTimeout = _options.KeepAliveTimeout;
        socket.Options.SetRequestHeader("User-Agent", $"rclicker/{AppInfo.Version}");

        // Corporate networks: use the system proxy (including PAC/WPAD) with the signed-in
        // user's Windows credentials, like a browser would.
        var proxy = HttpClient.DefaultProxy;
        proxy.Credentials = CredentialCache.DefaultCredentials;
        socket.Options.Proxy = proxy;
        return socket;
    }

    private string HostKeyFor(RemoteSession session)
    {
        lock (_gate)
        {
            if (!_hostKeys.TryGetValue(session.Generation, out var key))
            {
                // Private to this PC (never in the QR code): only this PC can reclaim its room after a network drop.
                key = SessionToken.Generate();
                _hostKeys.Clear();
                _hostKeys[session.Generation] = key;
            }

            return key;
        }
    }

    private async Task SendEncryptedAsync(ClientWebSocket socket, RelayCipher cipher, string pid, string plaintext, CancellationToken ct)
    {
        var (iv, ciphertext) = cipher.Encrypt(plaintext, RelayDirection.HostToPhone);
        await SendJsonAsync(socket, new { t = "msg", pid, iv, ct = ciphertext }, ct);
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[MaxRelayMessageChars];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaxRelayMessageChars * 4)
            {
                throw new WebSocketException("Relay message too large.");
            }

            if (result.EndOfMessage)
            {
                return result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(stream.ToArray()) : string.Empty;
            }
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Explain(Exception ex) => ex switch
    {
        OperationCanceledException or TimeoutException => "The relay did not answer in time. Retrying…",
        WebSocketException { InnerException: HttpRequestException http } => Explain(http),
        HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired } => "The network proxy refused the connection (proxy sign-in needed). Retrying…",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "The network blocked the relay address. Ask IT to allow it. Retrying…",
        HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } => "A secure (HTTPS) connection to the relay failed. Retrying…",
        HttpRequestException => "Can't reach the relay. Check the internet connection. Retrying…",
        WebSocketException => "The network does not allow the relay connection (WebSocket). Retrying…",
        _ => "Can't reach the relay. Retrying…",
    };

    private void SetStatus(RelayStatus status)
    {
        lock (_gate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, status);
        }
#pragma warning disable CA1031 // A UI handler bug must not break the connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "StatusChanged handler failed");
        }
    }

    private enum Freshness
    {
        Fresh,
        StaleNonce,
        Replayed,
        Invalid,
    }

    private sealed class PhoneChannel(string pid, string label, string nonce)
    {
        public string Pid { get; } = pid;

        public string Label { get; } = label;

        public string Nonce { get; } = nonce;

        public long LastId { get; set; }
    }
}

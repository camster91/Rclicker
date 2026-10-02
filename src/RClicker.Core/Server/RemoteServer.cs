using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RClicker.Presentation;
using RClicker.Sessions;

namespace RClicker.Server;

/// <summary>
/// The embedded Kestrel server: serves the phone page and the controller WebSocket.
/// Lives only while the receiver app runs.
/// </summary>
public sealed class RemoteServer : IAsyncDisposable
{
    private readonly RemoteServerOptions _options;
    private readonly SessionManager _sessions;
    private readonly ControllerHub _hub;
    private readonly CommandRouter _router;
    private readonly StaticAssets _assets;
    private WebApplication? _app;

    public RemoteServer(RemoteServerOptions options, SessionManager sessions, ControllerHub hub, CommandRouter router)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _assets = StaticAssets.LoadEmbedded();
    }

    /// <summary>The port actually in use (after any fallback). 0 until started.</summary>
    public int Port { get; private set; }

    public bool IsRunning => _app is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            throw new InvalidOperationException("Server already started.");
        }

        int attempts = _options.Port == 0 ? 1 : 1 + Math.Max(0, _options.PortFallbackAttempts);
        Exception? lastError = null;

        for (int i = 0; i < attempts; i++)
        {
            int port = _options.Port == 0 ? 0 : _options.Port + i;
            if (port > 65535)
            {
                break;
            }

            var app = Build(port);
            try
            {
                await app.StartAsync(cancellationToken);
                _app = app;
                Port = ResolveBoundPort(app, port);
                app.Logger.LogInformation("rclicker server listening on {Address}:{Port}", _options.BindAddress, Port);
                return;
            }
            catch (Exception ex) when (IsPortUnavailable(ex))
            {
                lastError = ex;
                app.Logger.LogWarning("Port {Port} is not available: {Message}", port, ex.Message);
                await app.DisposeAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        var range = attempts > 1 ? $"ports {_options.Port}–{_options.Port + attempts - 1} are" : $"port {_options.Port} is";
        throw new ServerStartException(
            $"Could not start the server: {range} already in use or blocked. Close the other program using the port, or start rclicker with --port <number>.",
            lastError!);
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        if (app is null)
        {
            return;
        }

        await _hub.ShutdownAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await app.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static bool IsPortUnavailable(Exception ex) =>
        ex is IOException or SocketException
        && (ex is Microsoft.AspNetCore.Connections.AddressInUseException
            || ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException or SocketException
            || ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied }
            || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase));

    private static int ResolveBoundPort(WebApplication app, int requested)
    {
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var first = addresses?.FirstOrDefault();
        return first is not null && Uri.TryCreate(first.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            ? uri.Port
            : requested;
    }

    private WebApplication Build(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RemoteServer).Assembly.GetName().Name,
            EnvironmentName = "Production",
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        _options.ConfigureLogging?.Invoke(builder.Logging);

        // ASP.NET Core request logs include the full URL (and therefore the session token). Keep them off.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

        // Port-in-use failures are caught below and logged in plain English; skip the host's stack dump.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Critical);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxConcurrentConnections = 64;
            kestrel.Limits.MaxConcurrentUpgradedConnections = 8;
            kestrel.Limits.MaxRequestBodySize = 4 * 1024;
            kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            kestrel.Limits.MaxRequestLineSize = 2 * 1024;
            kestrel.Listen(_options.BindAddress, port);
        });

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RClicker.Server");
        var endpoint = new ControllerEndpoint(_sessions, _hub, _router, _options, AppInfo.Version, logger);

        app.Use(RequestGuard.InvokeAsync);
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = _options.KeepAliveInterval,
            KeepAliveTimeout = _options.KeepAliveTimeout,
        });
        app.Run(context => DispatchAsync(context, endpoint));
        return app;
    }

    private Task DispatchAsync(HttpContext context, ControllerEndpoint endpoint)
    {
        var request = context.Request;
        var path = request.Path.Value;

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return Task.CompletedTask;
        }

        switch (path)
        {
            case "/ws":
                return endpoint.HandleAsync(context);

            case "/api/session":
                // Token in a header, not the URL. Used by the phone to tell "session ended" from "network down".
                return HttpResults.SessionStatusAsync(context, _sessions.Validate(request.Headers["X-Session-Token"].ToString()));
        }

        if (_assets.TryGet(path, out var asset))
        {
            context.Response.ContentType = asset.ContentType;
            context.Response.ContentLength = asset.Content.Length;
            if (asset.ContentType.StartsWith("text/html", StringComparison.Ordinal))
            {
                RequestGuard.AddPageSecurityHeaders(context);
            }

            return HttpMethods.IsHead(request.Method)
                ? Task.CompletedTask
                : context.Response.Body.WriteAsync(asset.Content, context.RequestAborted).AsTask();
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}

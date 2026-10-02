using System.Net;
using Microsoft.AspNetCore.Http;

namespace RClicker.Server;

/// <summary>
/// Cheap checks applied to every request before anything else runs.
/// </summary>
internal static class RequestGuard
{
    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // The QR URL always uses an IP address. Refusing host names blocks DNS-rebinding
        // tricks where a web page on some domain is pointed at this PC.
        if (!IsIpLiteralHost(context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsync("Use the address shown in the rclicker window.");
        }

        // Browsers send Origin on WebSocket and fetch requests. A page from another origin
        // must not be able to talk to the controller, even if it somehow had the token.
        if (!IsSameOriginOrAbsent(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.CacheControl = "no-store";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return next(context);
    }

    public static void AddPageSecurityHeaders(HttpContext context)
    {
        // Host was validated as an IP literal above, so it is safe to embed here.
        var host = context.Request.Host.Value;
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            $"connect-src 'self' ws://{host}; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    }

    internal static bool IsIpLiteralHost(HostString host)
    {
        if (!host.HasValue)
        {
            return false;
        }

        var name = host.Host;
        if (name.StartsWith('[') && name.EndsWith(']'))
        {
            name = name[1..^1];
        }

        return string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(name, out _);
    }

    internal static bool IsSameOriginOrAbsent(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

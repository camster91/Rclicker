using System.Net;

namespace RClicker.Relay;

/// <summary>Validates the relay address and builds the URLs the PC and phone use.</summary>
public static class RelayUrls
{
    /// <summary>
    /// Accepts https:// relays, and http:// only for this computer (local development with wrangler dev).
    /// Returns the normalised base URL ending in "/".
    /// </summary>
    public static bool TryParse(string? text, out Uri relay)
    {
        relay = null!;
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        bool local = uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        relay = builder.Uri;
        return true;
    }

    /// <summary>The QR code URL: the phone remote page, with the key in the #fragment (never sent to the server).</summary>
    public static Uri PhoneUrl(Uri relay, string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentException.ThrowIfNullOrEmpty(sessionToken);
        return new Uri(relay, "remote#k=" + sessionToken);
    }

    /// <summary>wss:// (or ws:// for local development) address of the PC's relay socket.</summary>
    public static Uri HostSocketUrl(Uri relay, string roomId)
    {
        ArgumentNullException.ThrowIfNull(relay);
        var builder = new UriBuilder(new Uri(relay, "ws/host?room=" + WebUtility.UrlEncode(roomId)))
        {
            Scheme = relay.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        };
        return builder.Uri;
    }
}

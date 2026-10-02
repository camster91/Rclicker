using Microsoft.AspNetCore.Http;
using PresentationRemote.Sessions;

namespace PresentationRemote.Server;

internal static class HttpResults
{
    /// <summary>200 for a valid session, 401 otherwise, with a tiny JSON body the phone understands.</summary>
    public static Task SessionStatusAsync(HttpContext context, SessionValidationResult validation)
    {
        context.Response.StatusCode = validation == SessionValidationResult.Valid
            ? StatusCodes.Status200OK
            : StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        var status = validation.ToString().ToLowerInvariant();
        return context.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
    }
}

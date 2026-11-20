namespace Backup.Server.Api;

/// <summary>
/// The server is for the person at this computer. Requests are accepted only if they were
/// addressed to this computer by name or number (so a page cannot reach it through DNS
/// rebinding) and, if they change something, only if they come from this very origin or from none.
/// </summary>
public static class HostGuard
{
    private static readonly string[] LocalNames = ["localhost", "127.0.0.1", "[::1]"];

    public static bool IsLocalHost(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader))
        {
            return false;
        }

        string host = hostHeader.StartsWith('[') ? hostHeader[..(hostHeader.IndexOf(']') + 1)] : hostHeader.Split(':')[0];
        return LocalNames.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsAllowedOrigin(string method, string? origin, string? hostHeader)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Authority, hostHeader, StringComparison.OrdinalIgnoreCase);
    }

    public static void Use(WebApplication app) => app.Use(async (context, next) =>
    {
        string? host = context.Request.Headers.Host;
        if (!IsLocalHost(host) || !IsAllowedOrigin(context.Request.Method, context.Request.Headers.Origin, host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    });
}

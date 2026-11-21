using Backup.Core;
using Backup.Infra;
using Backup.Infra.Logging;

namespace Backup.Server.Api;

public sealed record ApiError(string Code, string Message);

/// <summary>Turns what the application throws into an HTTP answer with a stable code the interface can translate.</summary>
public static class ErrorMapping
{
    public static void Use(WebApplication app) => app.Use(async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (DomainException error)
        {
            await Write(context, StatusOf(error.Code), new ApiError(error.Code, error.Message));
        }
        catch (ExternalToolException error)
        {
            // Known to happen (a wrong password, a remote that is down); no stack is needed.
            app.Logger.LogWarning("{Method} {Path} failed in an external tool ({Code}): {Message}", context.Request.Method, context.Request.Path, error.Code, error.Message);
            await Write(context, StatusCodes.Status502BadGateway, new ApiError(error.Code, error.Message));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            app.Logger.LogError(error, "{Method} {Path} failed unexpectedly.", context.Request.Method, context.Request.Path);
            // The person using the application sees what went wrong, not a request to read a log. Saved passwords are masked.
            await Write(context, StatusCodes.Status500InternalServerError, new ApiError("unexpected", Describe(app, error)));
        }
    });

    private static string Describe(WebApplication app, Exception error)
    {
        SecretRedactor redactor = new(app.Services.GetRequiredService<Workspace>()
            .Read((_, groups) => groups.Groups.SelectMany(g => g.Objects.Select(o => o.Password)).ToArray()));
        return redactor.Redact($"{error.GetType().Name}: {error.Message}");
    }

    internal static int StatusOf(string code) => code switch
    {
        _ when code.EndsWith("_not_found", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
        _ when code.EndsWith("_taken", StringComparison.Ordinal) || code.EndsWith("_exists", StringComparison.Ordinal)
            || code.EndsWith("_in_use", StringComparison.Ordinal) || code is "run_in_progress" or "plan_stale" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static async Task Write(HttpContext context, int status, ApiError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(error, ApiJson.Default.ApiError, cancellationToken: context.RequestAborted);
    }
}

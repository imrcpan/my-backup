using Backup.Infra.Process;
using Microsoft.Extensions.Configuration;

namespace Backup.Server.Api;

public sealed record ToolDto(bool Available, string? Version);

public sealed record SystemDto(ToolDto Restic, ToolDto Rclone);

public static class SystemEndpoints
{
    public static void Map(WebApplication app) => app.MapGet("/api/system", async (IProcessRunner runner, IConfiguration configuration, CancellationToken cancellationToken) =>
        new SystemDto(
            await Probe(runner, configuration["ResticPath"] ?? "restic", cancellationToken),
            await Probe(runner, configuration["RclonePath"] ?? "rclone", cancellationToken)));

    private static async Task<ToolDto> Probe(IProcessRunner runner, string program, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult result = await runner.RunAsync(new ProcessSpec(program, ["version"]), cancellationToken: cancellationToken);
            string? first = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return new ToolDto(result.Succeeded, first);
        }
        catch (Backup.Infra.ExternalToolException)
        {
            return new ToolDto(false, null);
        }
    }
}

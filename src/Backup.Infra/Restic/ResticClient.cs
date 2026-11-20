using System.Text.Json;
using Backup.Infra.Logging;
using Backup.Infra.Process;

namespace Backup.Infra.Restic;

/// <summary>
/// Runs restic. The repository location goes in the arguments, the password only in
/// the environment of the child process, so it is never on a command line.
/// </summary>
public sealed class ResticClient(IProcessRunner runner, ResticOptions? options = null) : IResticClient
{
    private readonly ResticOptions _options = options ?? new ResticOptions();

    public async Task<bool> ExistsAsync(ResticRepository repository, CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunRawAsync(repository, ["cat", "config"], null, cancellationToken);
        if (result.ExitCode == ExitRepositoryMissing)
        {
            return false;
        }

        Ensure(repository, result);
        return true;
    }

    public async Task InitAsync(ResticRepository repository, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(repository, cancellationToken))
        {
            throw new ExternalToolException("repo_exists", "A repository exists at this location already.");
        }

        Ensure(repository, await RunRawAsync(repository, ["init", "--json"], null, cancellationToken));
    }

    public async Task<IReadOnlyList<ResticSnapshot>> SnapshotsAsync(ResticRepository repository, CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunRawAsync(repository, ["snapshots", "--json"], null, cancellationToken);
        Ensure(repository, result);
        ResticSnapshot[] snapshots = JsonSerializer.Deserialize(result.StandardOutput, ResticJson.Default.ResticSnapshotArray) ?? [];
        return [.. snapshots.OrderBy(s => s.Time)];
    }

    public async Task<IReadOnlyList<ResticNode>> ListAsync(ResticRepository repository, string snapshotId, string directory, CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunRawAsync(repository, ["ls", "--json", snapshotId, directory], null, cancellationToken);
        Ensure(repository, result);
        // restic also prints the directory itself, which is not one of its children.
        string self = directory.Length > 1 ? directory.TrimEnd('/') : directory;
        List<ResticNode> nodes = [];
        foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("struct_type", out JsonElement kind) && kind.GetString() == "node")
            {
                ResticNode node = document.RootElement.Deserialize(ResticJson.Default.ResticNode)!;
                if (node.Path != self)
                {
                    nodes.Add(node);
                }
            }
        }

        return nodes;
    }

    internal const int ExitIncomplete = 3;
    internal const int ExitRepositoryMissing = 10;
    internal const int ExitLocked = 11;
    internal const int ExitWrongPassword = 12;

    internal Task<ProcessResult> RunRawAsync(
        ResticRepository repository,
        IReadOnlyList<string> arguments,
        Action<string>? onLine,
        CancellationToken cancellationToken,
        bool captureOutput = true)
    {
        List<string> all = ["-r", repository.Location, .. arguments];
        Dictionary<string, string?> environment = new()
        {
            ["RESTIC_PASSWORD"] = repository.Password,
            ["RESTIC_PROGRESS_FPS"] = "2",
            // Nothing from the user's own shell may point restic somewhere else.
            ["RESTIC_REPOSITORY"] = null,
            ["RESTIC_REPOSITORY_FILE"] = null,
            ["RESTIC_PASSWORD_FILE"] = null,
            ["RESTIC_PASSWORD_COMMAND"] = null,
            ["RESTIC_FROM_REPOSITORY"] = null,
        };
        return runner.RunAsync(new ProcessSpec(_options.ExecutablePath, all, environment, CaptureStandardOutput: captureOutput), onLine, null, cancellationToken);
    }

    /// <summary>Turns a failed run into an <see cref="ExternalToolException"/> with a stable code.</summary>
    internal static void Ensure(ResticRepository repository, ProcessResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        string code = result.ExitCode switch
        {
            ExitRepositoryMissing => "repo_missing",
            ExitLocked => "repo_locked",
            ExitWrongPassword => "repo_wrong_password",
            ExitIncomplete => "backup_incomplete",
            _ => "restic_failed",
        };
        string detail = new SecretRedactor([repository.Password]).Redact(result.StandardError).Trim();
        throw new ExternalToolException(code, detail.Length == 0 ? $"restic exited with code {result.ExitCode}." : detail, result.ExitCode);
    }
}

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

    public const int MaxPlanItems = 500;

    public async Task<ResticBackupPlan> PlanBackupAsync(ResticRepository repository, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        List<ResticPlanItem> items = [];
        long total = 0;
        JsonElement? summary = null;
        ProcessResult result = await RunRawAsync(
            repository,
            ["backup", "--json", "-vv", "--dry-run", "--", sourceDirectory],
            line =>
            {
                JsonElement? message = ParseLine(line);
                string? type = message?.GetProperty("message_type").GetString();
                if (type == "summary")
                {
                    summary = message;
                }
                else if (type == "verbose_status"
                    && message!.Value.GetProperty("action").GetString() is "new" or "modified"
                    && !message.Value.GetProperty("item").GetString()!.EndsWith('/'))
                {
                    total++;
                    if (items.Count < MaxPlanItems)
                    {
                        items.Add(new ResticPlanItem(
                            message.Value.GetProperty("action").GetString()!,
                            message.Value.GetProperty("item").GetString()!,
                            message.Value.GetProperty("data_size").GetInt64()));
                    }
                }
            },
            cancellationToken,
            captureOutput: false);
        Ensure(repository, result);
        JsonElement s = summary ?? throw new ExternalToolException("restic_output", "restic did not report a summary.");
        return new ResticBackupPlan(
            s.GetProperty("files_new").GetInt64(),
            s.GetProperty("files_changed").GetInt64(),
            s.GetProperty("files_unmodified").GetInt64(),
            s.GetProperty("total_bytes_processed").GetInt64(),
            s.GetProperty("data_added").GetInt64(),
            items,
            total > items.Count);
    }

    public async Task<ResticBackupSummary> BackupAsync(
        ResticRepository repository,
        string sourceDirectory,
        Action<ResticBackupProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        JsonElement? summary = null;
        List<string> errors = [];
        ProcessResult result = await RunRawAsync(
            repository,
            ["backup", "--json", "--", sourceDirectory],
            line =>
            {
                JsonElement? message = ParseLine(line);
                switch (message?.GetProperty("message_type").GetString())
                {
                    case "summary":
                        summary = message;
                        break;
                    case "status":
                        onProgress?.Invoke(new ResticBackupProgress(
                            Number(message.Value, "percent_done"),
                            (long)Number(message.Value, "files_done"),
                            (long)Number(message.Value, "total_files"),
                            (long)Number(message.Value, "bytes_done"),
                            (long)Number(message.Value, "total_bytes")));
                        break;
                    case "error":
                        errors.Add(ErrorText(message.Value));
                        break;
                }
            },
            cancellationToken,
            captureOutput: false);

        bool incomplete = result.ExitCode == ExitIncomplete;
        if (!incomplete)
        {
            Ensure(repository, result);
        }

        JsonElement s = summary ?? throw new ExternalToolException("restic_output", "restic did not report a summary.", result.ExitCode);
        return new ResticBackupSummary(
            s.GetProperty("snapshot_id").GetString() ?? "",
            s.GetProperty("files_new").GetInt64(),
            s.GetProperty("files_changed").GetInt64(),
            s.GetProperty("files_unmodified").GetInt64(),
            s.GetProperty("data_added").GetInt64(),
            s.GetProperty("total_bytes_processed").GetInt64(),
            incomplete,
            errors);
    }

    public async Task<ResticRestoreSummary> RestoreAsync(
        ResticRepository repository,
        string snapshotId,
        string targetDirectory,
        IReadOnlyList<string>? paths = null,
        Action<ResticRestoreProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(targetDirectory);
        if (File.Exists(target) || (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()))
        {
            throw new ExternalToolException("restore_target_not_empty", "The restore target must be a new or empty directory.");
        }

        List<string> arguments = ["restore", "--json", "--verify", snapshotId, "--target", target];
        foreach (string path in paths ?? [])
        {
            arguments.Add("--include");
            arguments.Add(EscapePattern(path));
        }

        JsonElement? summary = null;
        ProcessResult result = await RunRawAsync(
            repository,
            arguments,
            line =>
            {
                JsonElement? message = ParseLine(line);
                switch (message?.GetProperty("message_type").GetString())
                {
                    case "summary":
                        summary = message;
                        break;
                    case "status":
                        onProgress?.Invoke(new ResticRestoreProgress(
                            Number(message.Value, "percent_done"),
                            (long)Number(message.Value, "files_restored"),
                            (long)Number(message.Value, "total_files"),
                            (long)Number(message.Value, "bytes_restored"),
                            (long)Number(message.Value, "total_bytes")));
                        break;
                }
            },
            cancellationToken,
            captureOutput: false);
        Ensure(repository, result);
        JsonElement s = summary ?? throw new ExternalToolException("restic_output", "restic did not report a summary.");
        return new ResticRestoreSummary(
            s.GetProperty("files_restored").GetInt64(),
            s.GetProperty("total_files").GetInt64(),
            s.GetProperty("bytes_restored").GetInt64(),
            s.GetProperty("total_bytes").GetInt64());
    }

    /// <summary>A path is meant literally; the characters that restic reads as a pattern are escaped.</summary>
    internal static string EscapePattern(string path)
    {
        System.Text.StringBuilder result = new();
        foreach (char c in path)
        {
            if (c is '\\' or '*' or '?' or '[')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    private static JsonElement? ParseLine(string line)
    {
        try
        {
            JsonElement element = JsonDocument.Parse(line).RootElement.Clone();
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("message_type", out _) ? element : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static string ErrorText(JsonElement message)
    {
        string text = message.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("message", out JsonElement m)
            ? m.GetString() ?? ""
            : "";
        string item = message.TryGetProperty("item", out JsonElement i) ? i.GetString() ?? "" : "";
        return item.Length == 0 ? text : $"{item}: {text}";
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

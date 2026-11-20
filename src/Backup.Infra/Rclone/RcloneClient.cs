using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backup.Infra.Logging;
using Backup.Infra.Process;

namespace Backup.Infra.Rclone;

/// <param name="ConfigFile">Passed as --config; the default config of rclone if null.</param>
public sealed record RcloneOptions(string ExecutablePath = "rclone", string? ConfigFile = null);

public enum RemoteState
{
    /// <summary>rclone has no remote of this name.</summary>
    NotConfigured,

    /// <summary>The remote is configured but rclone could not reach it.</summary>
    Unreachable,

    Ready,
}

public sealed record RemoteStatus(RemoteState State, string? Message = null);

public sealed record RcloneEntry(string Name, bool IsDir);

internal sealed record RcloneJsonEntry(string Name, bool IsDir);

/// <param name="Action">"copy" or "delete".</param>
public sealed record RcloneSyncItem(string Action, string Path, long Size);

/// <summary>What a sync would do to the target. Nothing is changed by making this.</summary>
public sealed record RcloneSyncPlan(
    long CopyCount,
    long DeleteCount,
    long BytesToCopy,
    IReadOnlyList<RcloneSyncItem> Items,
    bool ItemsTruncated);

public sealed record RcloneSyncProgress(long Bytes, long TotalBytes, long Transfers, long TotalTransfers);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RcloneJsonEntry[]))]
internal sealed partial class RcloneJson : JsonSerializerContext;

public interface IRcloneClient
{
    Task<IReadOnlyList<string>> ListRemotesAsync(CancellationToken cancellationToken = default);

    Task<RemoteStatus> CheckRemoteAsync(string remote, CancellationToken cancellationToken = default);

    /// <summary>Lists the directories directly below a path of a remote.</summary>
    Task<IReadOnlyList<RcloneEntry>> ListDirectoriesAsync(string remote, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Google Drive remote. rclone opens a browser for the user to sign in
    /// and waits, so this ends when the user finishes or when it is cancelled.
    /// </summary>
    Task CreateGoogleDriveRemoteAsync(string remote, Action<string>? onLine = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds what making <paramref name="target"/> look like <paramref name="source"/> would copy and delete.
    /// Both are a directory or "remote:/path".
    /// </summary>
    Task<RcloneSyncPlan> PlanSyncAsync(string source, string target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the target look like the source. It stops before it deletes more than
    /// <paramref name="maxDeletes"/> files, which is what the user was shown and confirmed.
    /// </summary>
    Task SyncAsync(string source, string target, long maxDeletes, Action<RcloneSyncProgress>? onProgress = null, CancellationToken cancellationToken = default);
}

public sealed partial class RcloneClient(IProcessRunner runner, RcloneOptions? options = null) : IRcloneClient
{
    private readonly RcloneOptions _options = options ?? new RcloneOptions();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex RemoteNamePattern();

    public async Task<IReadOnlyList<string>> ListRemotesAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunAsync(["listremotes"], null, cancellationToken);
        Ensure(result);
        return [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => r.TrimEnd(':'))];
    }

    public async Task<RemoteStatus> CheckRemoteAsync(string remote, CancellationToken cancellationToken = default)
    {
        ValidateRemote(remote);
        if (!(await ListRemotesAsync(cancellationToken)).Contains(remote))
        {
            return new RemoteStatus(RemoteState.NotConfigured);
        }

        ProcessResult result = await RunAsync(["lsjson", "--dirs-only", "--max-depth", "1", "--contimeout", "15s", "--timeout", "30s", "--retries", "1", "--low-level-retries", "1", remote + ":"], null, cancellationToken);
        return result.Succeeded
            ? new RemoteStatus(RemoteState.Ready)
            : new RemoteStatus(RemoteState.Unreachable, Clean(result.StandardError));
    }

    public async Task<IReadOnlyList<RcloneEntry>> ListDirectoriesAsync(string remote, string path, CancellationToken cancellationToken = default)
    {
        ValidateRemote(remote);
        string normalized = Backup.Core.StoragePath.Normalize(path);
        ProcessResult result = await RunAsync(["lsjson", "--dirs-only", "--max-depth", "1", "--contimeout", "15s", "--timeout", "30s", "--retries", "1", "--low-level-retries", "1", remote + ":" + normalized], null, cancellationToken);
        Ensure(result);
        RcloneJsonEntry[] entries = JsonSerializer.Deserialize(result.StandardOutput, RcloneJson.Default.RcloneJsonEntryArray) ?? [];
        return [.. entries.Select(e => new RcloneEntry(e.Name, e.IsDir)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task CreateGoogleDriveRemoteAsync(string remote, Action<string>? onLine = null, CancellationToken cancellationToken = default)
    {
        ValidateRemote(remote);
        SecretRedactor redactor = SecretRedactor.None;
        ProcessResult result = await RunAsync(["config", "create", remote, "drive", "scope=drive"], line => onLine?.Invoke(redactor.Redact(line)), cancellationToken);
        Ensure(result);
    }

    public const int MaxPlanItems = 500;

    public async Task<RcloneSyncPlan> PlanSyncAsync(string source, string target, CancellationToken cancellationToken = default)
    {
        ValidateLocation(source);
        ValidateLocation(target);
        List<RcloneSyncItem> items = [];
        long copies = 0, deletes = 0, bytes = 0;
        ProcessResult result = await RunAsync(
            ["sync", "--dry-run", "--use-json-log", "-v", "--stats", "0", "--", source, target],
            null,
            cancellationToken,
            line =>
            {
                if (ParseSkipped(line) is not (string action, string path, long size) || action is not ("copy" or "delete"))
                {
                    return;
                }

                if (action == "copy")
                {
                    copies++;
                    bytes += size;
                }
                else
                {
                    deletes++;
                }

                if (items.Count < MaxPlanItems)
                {
                    items.Add(new RcloneSyncItem(action, path, size));
                }
            });
        Ensure(result);
        return new RcloneSyncPlan(copies, deletes, bytes, items, copies + deletes > items.Count);
    }

    public async Task SyncAsync(string source, string target, long maxDeletes, Action<RcloneSyncProgress>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ValidateLocation(source);
        ValidateLocation(target);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDeletes);
        ProcessResult result = await RunAsync(
            [
                "sync", "--use-json-log", "--stats", "1s", "--stats-log-level", "NOTICE",
                "--max-delete", maxDeletes.ToString(System.Globalization.CultureInfo.InvariantCulture), "--", source, target,
            ],
            null,
            cancellationToken,
            line =>
            {
                if (ParseStats(line) is RcloneSyncProgress progress)
                {
                    onProgress?.Invoke(progress);
                }
            });
        Ensure(result);
    }

    private static (string Action, string Path, long Size)? ParseSkipped(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("skipped", out JsonElement action) && root.TryGetProperty("object", out JsonElement path))
            {
                long size = root.TryGetProperty("size", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                return (action.GetString() ?? "", path.GetString() ?? "", size);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static RcloneSyncProgress? ParseStats(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("stats", out JsonElement stats))
            {
                static long Get(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                return new RcloneSyncProgress(Get(stats, "bytes"), Get(stats, "totalBytes"), Get(stats, "transfers"), Get(stats, "totalTransfers"));
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    [GeneratedRegex("^(/|[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}:/)")]
    private static partial Regex LocationPattern();

    /// <summary>A directory or "remote:/path"; never something that could be read as an option.</summary>
    private static void ValidateLocation(string location)
    {
        if (!LocationPattern().IsMatch(location))
        {
            throw new ExternalToolException("location_invalid", "A location must be an absolute path or remote:/path.");
        }
    }

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, Action<string>? onLine, CancellationToken cancellationToken, Action<string>? onErrorLine = null)
    {
        List<string> all = [];
        if (_options.ConfigFile != null)
        {
            all.Add("--config");
            all.Add(_options.ConfigFile);
        }

        all.AddRange(arguments);
        return runner.RunAsync(new ProcessSpec(_options.ExecutablePath, all), onLine, onErrorLine, cancellationToken);
    }

    private static void ValidateRemote(string remote)
    {
        if (!RemoteNamePattern().IsMatch(remote))
        {
            throw new ExternalToolException("remote_invalid", "The remote name is not valid.");
        }
    }

    private static void Ensure(ProcessResult result)
    {
        if (!result.Succeeded)
        {
            throw new ExternalToolException("rclone_failed", Clean(result.StandardError), result.ExitCode);
        }
    }

    private static string Clean(string error)
    {
        string text = SecretRedactor.None.Redact(error).Trim();
        return text.Length == 0 ? "rclone failed." : text;
    }
}

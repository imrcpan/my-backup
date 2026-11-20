using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Planning;

public sealed partial class RunPlanner
{
    /// <summary>Finds a snapshot by its id or by the start of it, if it names exactly one.</summary>
    private static ResticSnapshot Pick(IReadOnlyList<ResticSnapshot> snapshots, string? id)
    {
        ResticSnapshot[] found = string.IsNullOrWhiteSpace(id)
            ? []
            : [.. snapshots.Where(s => s.Id.StartsWith(id.Trim(), StringComparison.OrdinalIgnoreCase))];
        return found.Length == 1
            ? found[0]
            : throw new DomainException("snapshot_not_found", "The snapshot does not exist or the id is not exact.");
    }

    /// <summary>An encrypted object is a restic repository; this finds it, checks it is there and that the password opens it.</summary>
    private async Task<(ResolvedLocation Location, ResticRepository Repository)> OpenRepositoryAsync(
        Snapshot snapshot, StorageObject item, bool forWrite, CancellationToken cancellationToken)
    {
        if (!item.Encrypted)
        {
            throw new DomainException("object_not_a_repository", "Only an encrypted object holds snapshots.");
        }

        ResolvedLocation location = Resolve(snapshot, item, forWrite);
        await EnsureReadyAsync(location, cancellationToken);
        ResticRepository repository = Repository(location);
        if (!await restic.ExistsAsync(repository, cancellationToken))
        {
            throw new DomainException("repo_missing", "There is no repository at this place yet.");
        }

        return (location, repository);
    }

    /// <param name="snapshotId">A snapshot of the repository.</param>
    /// <param name="targetDirectory">An absolute path of a directory that does not exist or is empty.</param>
    /// <param name="paths">What to restore; the whole snapshot if empty.</param>
    public async Task<Run> PlanRestoreAsync(
        string groupId, string objectId, string? snapshotId, string? targetDirectory, IReadOnlyList<string>? paths, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = Take(groupId);
        StorageObject item = FindObject(snapshot, objectId);
        Run run = NewRun(RunKind.Restore, snapshot, "", [item]);
        run.Steps.Add(await GuardAsync(StepKind.Restore, item.Id, async step =>
        {
            (ResolvedLocation location, ResticRepository repository) = await OpenRepositoryAsync(snapshot, item, false, cancellationToken);
            ResticSnapshot chosen = Pick(await restic.SnapshotsAsync(repository, cancellationToken), snapshotId);

            string target = NewDirectory(targetDirectory);
            if (location.FileSystemPath != null && Inside(target, location.FileSystemPath))
            {
                throw new DomainException("restore_target_in_repository", "The target lies inside the repository.");
            }

            List<string> chosenPaths = [.. (paths ?? []).Select(AbsolutePath).Distinct()];
            step.Arguments.SnapshotId = chosen.Id;
            step.Arguments.TargetDirectory = target;
            step.Arguments.Paths = chosenPaths;
            if (chosenPaths.Count == 0)
            {
                step.ToAdd = chosen.Summary?.TotalFilesProcessed ?? 0;
                step.Bytes = chosen.Summary?.TotalBytesProcessed ?? 0;
                step.Lines.Add(new PlanLine { Action = "restore", Path = $"{chosen.ShortId} ({chosen.Time:yyyy-MM-dd HH:mm})", Size = step.Bytes });
            }
            else
            {
                step.ToAdd = chosenPaths.Count;
                AddLines(step, chosenPaths.Take(MaxLines).Select(p => new PlanLine { Action = "restore", Path = p }), chosenPaths.Count > MaxLines);
            }
        }));
        return run;
    }

    /// <summary>Restic restores below the target with the full path of each file; the target must be new.</summary>
    private static string NewDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !directory.Trim().StartsWith('/') || directory.Contains('\0'))
        {
            throw new DomainException("restore_target_invalid", "The target must be an absolute path.");
        }

        string full = Path.GetFullPath(directory.Trim());
        if (File.Exists(full) || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()))
        {
            throw new DomainException("restore_target_not_empty", "A snapshot is restored only into a new or empty directory.");
        }

        return full;
    }

    private static string AbsolutePath(string path)
    {
        string trimmed = path.Trim();
        if (!trimmed.StartsWith('/') || trimmed.Contains('\0') || trimmed.Split('/').Contains(".."))
        {
            throw new DomainException("path_invalid", "A path of a snapshot must be absolute and without '..'.");
        }

        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    /// <summary>
    /// Plans the removal of snapshots, either the ones named or those that the retention policy of the
    /// object drops. The run removes exactly the snapshots that the plan lists.
    /// </summary>
    /// <param name="snapshotIds">The snapshots to remove; the policy of the object decides if null.</param>
    /// <param name="pruneAfter">Also delete the data that is no longer used, as a step of its own.</param>
    public async Task<Run> PlanForgetAsync(string groupId, string objectId, IReadOnlyList<string>? snapshotIds, bool pruneAfter, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = Take(groupId);
        StorageObject item = FindObject(snapshot, objectId);
        Run run = NewRun(RunKind.Forget, snapshot, "", [item]);
        run.Steps.Add(await GuardAsync(StepKind.Forget, item.Id, async step =>
        {
            (_, ResticRepository repository) = await OpenRepositoryAsync(snapshot, item, true, cancellationToken);
            ResticForgetPlan plan;
            if (snapshotIds == null)
            {
                RetentionPolicy policy = item.Retention ?? throw new DomainException("retention_not_set", "This repository has no retention policy.");
                plan = await restic.PlanForgetAsync(repository, policy, cancellationToken);
                step.Arguments.Retention = policy;
            }
            else
            {
                plan = await restic.PlanForgetSnapshotsAsync(repository, snapshotIds, cancellationToken);
            }

            step.Arguments.SnapshotIds = [.. plan.Remove.Select(s => s.Id)];
            step.Arguments.PruneAfter = pruneAfter;
            step.ToDelete = plan.Remove.Count;
            List<PlanLine> lines =
            [
                .. plan.Remove.Select(s => new PlanLine { Action = "remove", Path = $"{s.ShortId} ({s.Time:yyyy-MM-dd HH:mm})", Size = s.Summary?.TotalBytesProcessed ?? 0 }),
                .. plan.Keep.Select(s => new PlanLine { Action = "keep", Path = $"{s.ShortId} ({s.Time:yyyy-MM-dd HH:mm})", Size = s.Summary?.TotalBytesProcessed ?? 0 }),
            ];
            AddLines(step, lines.Take(MaxLines), lines.Count > MaxLines);
            if (plan.Remove.Count == 0)
            {
                step.State = StepState.UpToDate;
            }
            else if (pruneAfter)
            {
                step.Notes.Add("prunes_after");
            }
        }));
        return run;
    }

    /// <summary>Deletes the data that no snapshot uses. It cannot be undone, and it needs its own confirmation.</summary>
    public async Task<Run> PlanPruneAsync(string groupId, string objectId, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = Take(groupId);
        StorageObject item = FindObject(snapshot, objectId);
        Run run = NewRun(RunKind.Prune, snapshot, "", [item]);
        run.Steps.Add(await GuardAsync(StepKind.Prune, item.Id, async step =>
        {
            await OpenRepositoryAsync(snapshot, item, true, cancellationToken);
            step.Notes.Add("irreversible");
        }));
        return run;
    }

    public async Task<Run> PlanCheckAsync(string groupId, string objectId, CheckDepth depth, int subsetPercent, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = Take(groupId);
        StorageObject item = FindObject(snapshot, objectId);
        Run run = NewRun(RunKind.Check, snapshot, "", [item]);
        run.Steps.Add(await GuardAsync(StepKind.Check, item.Id, async step =>
        {
            if (depth == CheckDepth.Subset && subsetPercent is < 1 or > 100)
            {
                throw new DomainException("check_subset_invalid", "The share to read must be between 1 and 100 percent.");
            }

            await OpenRepositoryAsync(snapshot, item, false, cancellationToken);
            step.Arguments.CheckDepth = depth;
            step.Arguments.SubsetPercent = subsetPercent;
            if (depth != CheckDepth.Structure)
            {
                step.Notes.Add(depth == CheckDepth.Full ? "reads_all_data" : "reads_some_data");
            }
        }));
        return run;
    }
}

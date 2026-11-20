using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Planning;

public sealed partial class RunPlanner
{
    /// <summary>
    /// Plans a run that brings the targets up to date from the object with the newest copy. Each target
    /// is a step of its own, and how it is reached depends on the encryption of both sides.
    /// </summary>
    /// <param name="targetIds">The targets; all other objects of the group if null.</param>
    public async Task<Run> PlanSyncAsync(string groupId, string? sourceId, IReadOnlyList<string>? targetIds, CancellationToken cancellationToken = default)
    {
        Snapshot snapshot = Take(groupId);
        SyncSelection selection = SyncRules.Select(snapshot.Group, sourceId, targetIds);
        Run run = NewRun(RunKind.Sync, snapshot, selection.Source.Id, [selection.Source, .. selection.Targets]);

        foreach (StorageObject target in selection.Targets)
        {
            SyncKind kind = SyncRules.KindFor(selection.Source, target);
            StepKind stepKind = kind switch
            {
                SyncKind.ResticBackup => StepKind.ResticBackup,
                SyncKind.ResticCopy => StepKind.ResticCopy,
                SyncKind.FileSync => StepKind.FileSync,
                _ => StepKind.RestoreToDirectory,
            };
            run.Steps.Add(await GuardAsync(stepKind, target.Id, step => kind switch
            {
                SyncKind.ResticBackup => PlanResticBackupAsync(snapshot, selection.Source, target, step, cancellationToken),
                SyncKind.ResticCopy => PlanResticCopyAsync(snapshot, selection.Source, target, step, cancellationToken),
                SyncKind.FileSync => PlanFileSyncAsync(snapshot, selection.Source, target, step, cancellationToken),
                _ => PlanRestoreToDirectoryAsync(snapshot, selection.Source, target, step, cancellationToken),
            }));
        }

        return run;
    }

    private async Task PlanResticBackupAsync(Snapshot snapshot, StorageObject source, StorageObject target, RunStep step, CancellationToken cancellationToken)
    {
        ResolvedLocation from = Resolve(snapshot, source, forWrite: false);
        ResolvedLocation to = Resolve(snapshot, target, forWrite: true);
        string directory = from.FileSystemPath ?? throw new DomainException("source_not_a_directory", "The source is not a directory.");
        SourceGuard.EnsureUsable(directory);
        if (to.FileSystemPath != null && (Inside(to.FileSystemPath, directory) || Inside(directory, to.FileSystemPath)))
        {
            throw new DomainException("sync_overlap", "The target and the source lie inside each other.");
        }

        await EnsureReadyAsync(to, cancellationToken);
        ResticRepository repository = Repository(to);
        if (await restic.ExistsAsync(repository, cancellationToken))
        {
            ResticBackupPlan plan = await restic.PlanBackupAsync(repository, directory, cancellationToken);
            step.ToAdd = plan.FilesNew;
            step.ToChange = plan.FilesChanged;
            step.Bytes = plan.BytesToUpload;
            AddLines(step, plan.Items.Select(i => new PlanLine { Action = i.Action, Path = i.Path, Size = i.Size }), plan.ItemsTruncated);
            if (plan.FilesNew + plan.FilesChanged == 0)
            {
                step.State = StepState.UpToDate;
            }

            return;
        }

        // The repository is new, so restic cannot say what it would store; the source is counted instead.
        if (to.FileSystemPath != null && Directory.Exists(to.FileSystemPath) && Directory.EnumerateFileSystemEntries(to.FileSystemPath).Any())
        {
            throw new DomainException("target_not_empty", "The target directory holds other files and no repository. A new repository is not made inside it.");
        }

        DirectoryScan scan = DirectoryScanner.Scan(directory, MaxLines);
        step.Notes.Add("creates_repository");
        step.ToAdd = scan.Files;
        step.Bytes = scan.Bytes;
        AddLines(step, scan.First.Select(f => new PlanLine { Action = "new", Path = f.Path, Size = f.Size }), scan.Truncated);
    }

    private async Task PlanResticCopyAsync(Snapshot snapshot, StorageObject source, StorageObject target, RunStep step, CancellationToken cancellationToken)
    {
        ResolvedLocation from = Resolve(snapshot, source, forWrite: false);
        ResolvedLocation to = Resolve(snapshot, target, forWrite: true);
        await EnsureReadyAsync(from, cancellationToken);
        await EnsureReadyAsync(to, cancellationToken);

        ResticSnapshot latest = await LatestSnapshotAsync(Repository(from), cancellationToken);
        ResticRepository destination = Repository(to);
        if (await restic.ExistsAsync(destination, cancellationToken))
        {
            IReadOnlyList<ResticSnapshot> present = await restic.SnapshotsAsync(destination, cancellationToken);
            if (present.Any(s => s.SameContentAs(latest)))
            {
                step.State = StepState.UpToDate;
                step.Arguments.SnapshotId = latest.Id;
                return;
            }
        }
        else
        {
            step.Notes.Add("creates_repository");
        }

        long bytes = latest.Summary?.TotalBytesProcessed ?? 0;
        step.Arguments.SnapshotId = latest.Id;
        step.ToAdd = 1;
        step.Bytes = bytes;
        step.Lines.Add(new PlanLine { Action = "copy", Path = $"{latest.ShortId} ({latest.Time:yyyy-MM-dd HH:mm})", Size = bytes });
    }

    private async Task PlanFileSyncAsync(Snapshot snapshot, StorageObject source, StorageObject target, RunStep step, CancellationToken cancellationToken)
    {
        ResolvedLocation from = Resolve(snapshot, source, forWrite: false);
        ResolvedLocation to = Resolve(snapshot, target, forWrite: true);
        string directory = from.FileSystemPath ?? throw new DomainException("source_not_a_directory", "The source is not a directory.");
        SourceGuard.EnsureUsable(directory);
        if (to.FileSystemPath != null && (Inside(to.FileSystemPath, directory) || Inside(directory, to.FileSystemPath)))
        {
            throw new DomainException("sync_overlap", "The target and the source lie inside each other.");
        }

        RcloneSyncPlan plan = await rclone.PlanSyncAsync(from.RcloneSpec, to.RcloneSpec, cancellationToken);
        step.ToAdd = plan.CopyCount;
        step.ToDelete = plan.DeleteCount;
        step.Bytes = plan.BytesToCopy;
        AddLines(step, plan.Items.Select(i => new PlanLine { Action = i.Action, Path = i.Path, Size = i.Size }), plan.ItemsTruncated);
        if (plan.DeleteCount > 0)
        {
            step.Notes.Add("deletes_in_target");
        }

        if (plan.CopyCount + plan.DeleteCount == 0)
        {
            step.State = StepState.UpToDate;
        }
    }

    private async Task PlanRestoreToDirectoryAsync(Snapshot snapshot, StorageObject source, StorageObject target, RunStep step, CancellationToken cancellationToken)
    {
        ResolvedLocation from = Resolve(snapshot, source, forWrite: false);
        ResolvedLocation to = Resolve(snapshot, target, forWrite: true);
        await EnsureReadyAsync(from, cancellationToken);
        string directory = to.FileSystemPath ?? throw new DomainException("target_not_a_directory", "The target is not a directory.");
        if (File.Exists(directory) || (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()))
        {
            throw new DomainException("restore_target_not_empty", "A snapshot is restored only into a new or empty directory.");
        }

        ResticSnapshot latest = await LatestSnapshotAsync(Repository(from), cancellationToken);
        step.Arguments.SnapshotId = latest.Id;
        step.Arguments.SnapshotPath = latest.Paths.Length == 1 ? latest.Paths[0] : null;
        step.ToAdd = latest.Summary?.TotalFilesProcessed ?? 0;
        step.Bytes = latest.Summary?.TotalBytesProcessed ?? 0;
        step.Lines.Add(new PlanLine { Action = "restore", Path = $"{latest.ShortId} ({latest.Time:yyyy-MM-dd HH:mm})", Size = step.Bytes });
    }

    private async Task<ResticSnapshot> LatestSnapshotAsync(ResticRepository repository, CancellationToken cancellationToken)
    {
        if (!await restic.ExistsAsync(repository, cancellationToken))
        {
            throw new DomainException("source_repository_missing", "The repository with the newest copy does not exist.");
        }

        IReadOnlyList<ResticSnapshot> snapshots = await restic.SnapshotsAsync(repository, cancellationToken);
        return snapshots.Count > 0
            ? snapshots[^1]
            : throw new DomainException("source_has_no_snapshots", "The repository with the newest copy has no snapshot.");
    }
}

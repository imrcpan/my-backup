using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra;
using Backup.Infra.Logging;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Execution;

public sealed partial class RunService
{
    /// <summary>The group and its devices when the run began, and the redactor for the passwords of its objects.</summary>
    private sealed record Context(BackupGroup Group, IReadOnlyDictionary<string, StorageDevice> Devices, SecretRedactor Redactor, string SourceObjectId);

    private async Task ExecuteAsync(string runId, CancellationToken cancellationToken)
    {
        SecretRedactor redactor = SecretRedactor.None;
        bool cancelled = false, failed = false;
        try
        {
            Context context = BuildContext(runId);
            redactor = context.Redactor;
            Log(runId, redactor, $"Run started ({runs.Read(runId, r => r.Kind)}) for the group '{context.Group.Name}'.");

            string[] stepIds = runs.Read(runId, r => r.Steps.Select(s => s.Id).ToArray());
            foreach (string stepId in stepIds)
            {
                RunStep step = runs.Read(runId, r => r.Steps.First(s => s.Id == stepId));
                if (cancelled)
                {
                    SetStep(runId, step, StepState.Skipped);
                    continue;
                }

                if (step.State == StepState.UpToDate)
                {
                    Log(runId, redactor, $"{Describe(step, context)}: already up to date.");
                    continue;
                }

                SetStep(runId, step, StepState.Running);
                Log(runId, redactor, $"{Describe(step, context)}: started.");
                try
                {
                    await RunStepAsync(context, runId, step, cancellationToken);
                    runs.Update(runId, _ =>
                    {
                        step.State = StepState.Succeeded;
                        step.Progress = 1;
                    });
                    Log(runId, redactor, $"{Describe(step, context)}: done.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    SetStep(runId, step, StepState.Cancelled);
                    Log(runId, redactor, $"{Describe(step, context)}: cancelled.");
                    cancelled = true;
                }
                catch (DomainException error)
                {
                    failed = true;
                    Fail(runId, step, redactor, error.Code, error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: failed ({error.Code}): {error.Message}");
                }
                catch (ExternalToolException error)
                {
                    failed = true;
                    Fail(runId, step, redactor, error.Code, error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: failed ({error.Code}): {error.Message}");
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Not an error that is known to happen, so the stack is kept for whoever has to find it.
                    failed = true;
                    Fail(runId, step, redactor, "unexpected", error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: unexpected error: {error}");
                }
            }
        }
        catch (Exception error)
        {
            failed = true;
            Log(runId, redactor, $"The run could not be carried out: {error}");
            runs.Update(runId, r => r.ErrorMessage = redactor.Redact(error.Message));
        }
        finally
        {
            runs.Update(runId, run =>
            {
                run.State = cancelled ? RunState.Cancelled : failed ? RunState.Failed : RunState.Succeeded;
                run.FinishedAt = time.GetUtcNow();
                foreach (RunStep leftover in run.Steps.Where(s => s.State is StepState.Pending or StepState.Running))
                {
                    leftover.State = StepState.Skipped;
                }
            });
            Log(runId, redactor, $"Run ended: {runs.Read(runId, r => r.State)}.");
            lock (_gate)
            {
                _active.Remove(runId);
            }
        }
    }

    private Context BuildContext(string runId)
    {
        Run run = runs.Find(runId)!;
        return workspace.Read((devices, groups) =>
        {
            BackupGroup group = groups.Get(run.GroupId);
            return new Context(
                new BackupGroup { Id = group.Id, Name = group.Name, Objects = [.. group.Objects] },
                devices.Devices.ToDictionary(d => d.Id),
                new SecretRedactor(group.Objects.Select(o => o.Password)),
                run.SourceObjectId);
        });
    }

    private static string Describe(RunStep step, Context context)
    {
        StorageObject? target = context.Group.Objects.Find(o => o.Id == step.TargetObjectId);
        return $"{step.Kind} {target?.DeviceId}:{target?.Path}";
    }

    private void Log(string runId, SecretRedactor redactor, string line) => log.Append(runId, line, redactor);

    private void SetStep(string runId, RunStep step, StepState state) => runs.Update(runId, _ => step.State = state);

    private void Fail(string runId, RunStep step, SecretRedactor redactor, string code, string message) => runs.Update(runId, _ =>
    {
        step.State = StepState.Failed;
        step.ErrorCode = code;
        step.ErrorMessage = redactor.Redact(message);
    });

    private void Progress(string runId, RunStep step, double fraction, long bytesDone) =>
        runs.Touch(runId, _ =>
        {
            step.Progress = Math.Clamp(fraction, 0, 1);
            step.BytesDone = bytesDone;
        });

    private ResolvedLocation Resolve(Context context, string objectId, bool forWrite)
    {
        StorageObject item = context.Group.Objects.Find(o => o.Id == objectId)
            ?? throw new DomainException("object_not_found", "The storage object does not exist.");
        StorageDevice device = context.Devices.GetValueOrDefault(item.DeviceId)
            ?? throw new DomainException("device_not_found", "The storage device does not exist.");
        return resolver.Resolve(item, device, forWrite);
    }

    private static ResticRepository Repository(ResolvedLocation location) => new(location.ResticLocation, location.Object.Password!);

    private async Task RunStepAsync(Context context, string runId, RunStep step, CancellationToken cancellationToken)
    {
        SecretRedactor redactor = context.Redactor;
        void Line(string text) => Log(runId, redactor, text);

        switch (step.Kind)
        {
            case StepKind.ResticBackup:
                {
                    ResolvedLocation from = Resolve(context, context.SourceObjectId, forWrite: false);
                    ResolvedLocation to = Resolve(context, step.TargetObjectId, forWrite: true);
                    string directory = from.FileSystemPath ?? throw new DomainException("source_not_a_directory", "The source is not a directory.");
                    SourceGuard.EnsureUsable(directory);
                    await EnsureRemoteAsync(to, cancellationToken);
                    ResticRepository repository = Repository(to);
                    if (!await restic.ExistsAsync(repository, cancellationToken))
                    {
                        Line("Creating the encrypted repository.");
                        await restic.InitAsync(repository, cancellationToken);
                    }

                    ResticBackupSummary summary = await restic.BackupAsync(
                        repository, directory, p => Progress(runId, step, p.PercentDone, p.BytesDone), cancellationToken);
                    Line($"Snapshot {summary.SnapshotId[..Math.Min(8, summary.SnapshotId.Length)]}: {summary.FilesNew} new, {summary.FilesChanged} changed, {summary.DataAdded} bytes added.");
                    if (summary.Incomplete)
                    {
                        runs.Update(runId, _ => step.Notes.Add("backup_incomplete"));
                        foreach (string error in summary.Errors.Take(50))
                        {
                            Line("Not saved: " + error);
                        }
                    }

                    break;
                }

            case StepKind.ResticCopy:
                {
                    ResolvedLocation from = Resolve(context, context.SourceObjectId, forWrite: false);
                    ResolvedLocation to = Resolve(context, step.TargetObjectId, forWrite: true);
                    await EnsureRemoteAsync(from, cancellationToken);
                    await EnsureRemoteAsync(to, cancellationToken);
                    ResticRepository source = Repository(from);
                    ResticRepository target = Repository(to);
                    if (!await restic.ExistsAsync(target, cancellationToken))
                    {
                        Line("Creating the encrypted repository with the chunking of the source.");
                        await restic.InitFromAsync(target, source, cancellationToken);
                    }

                    await restic.CopyAsync(target, source, step.Arguments.SnapshotId!, Line, cancellationToken);
                    break;
                }

            case StepKind.FileSync:
                {
                    ResolvedLocation from = Resolve(context, context.SourceObjectId, forWrite: false);
                    ResolvedLocation to = Resolve(context, step.TargetObjectId, forWrite: true);
                    SourceGuard.EnsureUsable(from.FileSystemPath ?? throw new DomainException("source_not_a_directory", "The source is not a directory."));
                    // At most the deletions that were shown to the user, whatever the source looks like now.
                    await rclone.SyncAsync(
                        from.RcloneSpec, to.RcloneSpec, step.ToDelete,
                        p => Progress(runId, step, p.TotalBytes > 0 ? (double)p.Bytes / p.TotalBytes : 0, p.Bytes), cancellationToken);
                    break;
                }

            case StepKind.RestoreToDirectory:
                {
                    ResolvedLocation from = Resolve(context, context.SourceObjectId, forWrite: false);
                    ResolvedLocation to = Resolve(context, step.TargetObjectId, forWrite: true);
                    await EnsureRemoteAsync(from, cancellationToken);
                    string snapshot = step.Arguments.SnapshotPath == null ? step.Arguments.SnapshotId! : $"{step.Arguments.SnapshotId}:{step.Arguments.SnapshotPath}";
                    await restic.RestoreAsync(
                        Repository(from), snapshot, to.FileSystemPath ?? throw new DomainException("target_not_a_directory", "The target is not a directory."),
                        null, p => Progress(runId, step, p.PercentDone, p.BytesRestored), cancellationToken);
                    break;
                }

            case StepKind.Restore:
                {
                    ResolvedLocation from = Resolve(context, step.TargetObjectId, forWrite: false);
                    await EnsureRemoteAsync(from, cancellationToken);
                    await restic.RestoreAsync(
                        Repository(from), step.Arguments.SnapshotId!, step.Arguments.TargetDirectory!,
                        step.Arguments.Paths.Count == 0 ? null : step.Arguments.Paths,
                        p => Progress(runId, step, p.PercentDone, p.BytesRestored), cancellationToken);
                    break;
                }

            case StepKind.Forget:
                {
                    ResolvedLocation location = Resolve(context, step.TargetObjectId, forWrite: true);
                    await EnsureRemoteAsync(location, cancellationToken);
                    ResticRepository repository = Repository(location);
                    await restic.ForgetSnapshotsAsync(repository, step.Arguments.SnapshotIds, cancellationToken);
                    Line($"Removed {step.Arguments.SnapshotIds.Count} snapshot(s).");
                    if (step.Arguments.PruneAfter)
                    {
                        await restic.PruneAsync(repository, Line, cancellationToken);
                    }

                    break;
                }

            case StepKind.Prune:
                {
                    ResolvedLocation location = Resolve(context, step.TargetObjectId, forWrite: true);
                    await EnsureRemoteAsync(location, cancellationToken);
                    await restic.PruneAsync(Repository(location), Line, cancellationToken);
                    break;
                }

            case StepKind.Check:
                {
                    ResolvedLocation location = Resolve(context, step.TargetObjectId, forWrite: false);
                    await EnsureRemoteAsync(location, cancellationToken);
                    ResticCheckMode mode = step.Arguments.CheckDepth switch
                    {
                        CheckDepth.Full => ResticCheckMode.Full,
                        CheckDepth.Subset => ResticCheckMode.Subset,
                        _ => ResticCheckMode.Structure,
                    };
                    ResticCheckResult result = await restic.CheckAsync(Repository(location), mode, step.Arguments.SubsetPercent, Line, cancellationToken);
                    if (!result.Ok)
                    {
                        throw new DomainException("check_failed", "The repository has errors. " + result.Output);
                    }

                    break;
                }
        }
    }

    private async Task EnsureRemoteAsync(ResolvedLocation location, CancellationToken cancellationToken)
    {
        if (location.RcloneRemote == null)
        {
            return;
        }

        RemoteStatus status = await rclone.CheckRemoteAsync(location.RcloneRemote, cancellationToken);
        if (status.State != RemoteState.Ready)
        {
            throw new DomainException(
                status.State == RemoteState.NotConfigured ? "remote_not_configured" : "remote_unreachable",
                status.Message ?? $"The remote '{location.RcloneRemote}' is not ready.");
        }
    }
}

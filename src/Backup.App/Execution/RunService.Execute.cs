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
                    _restoreOutputs.TryRemove(step.Id, out _);
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
                    await CleanUpAsync(context, runId, step);
                }
                catch (DomainException error)
                {
                    failed = true;
                    Fail(runId, step, redactor, error.Code, error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: failed ({error.Code}): {error.Message}");
                    await CleanUpAsync(context, runId, step);
                }
                catch (ExternalToolException error)
                {
                    failed = true;
                    Fail(runId, step, redactor, error.Code, error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: failed ({error.Code}): {error.Message}");
                    await CleanUpAsync(context, runId, step);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Not an error that is known to happen, so the stack is kept for whoever has to find it.
                    failed = true;
                    Fail(runId, step, redactor, "unexpected", error.Message);
                    Log(runId, redactor, $"{Describe(step, context)}: unexpected error: {error}");
                    await CleanUpAsync(context, runId, step);
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

                        // restic saves the snapshot of what it could read as if it were whole. Marked, it is not taken
                        // for a complete copy when the newest one is chosen, and a clean-up never leaves it alone in the repository.
                        try
                        {
                            await restic.TagAsync(repository, summary.SnapshotId, ResticSnapshot.IncompleteTag, cancellationToken);
                            Line("The snapshot is marked as incomplete.");
                        }
                        catch (ExternalToolException error)
                        {
                            runs.Update(runId, _ => step.Notes.Add("incomplete_not_marked"));
                            Line("The snapshot could not be marked as incomplete: " + error.Message);
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
                    string directory = to.FileSystemPath ?? throw new DomainException("target_not_a_directory", "The target is not a directory.");
                    BeginRestoreOutput(step, directory);
                    await restic.RestoreAsync(
                        Repository(from), snapshot, directory,
                        null, p => Progress(runId, step, p.PercentDone, p.BytesRestored), cancellationToken);
                    break;
                }

            case StepKind.Restore:
                {
                    ResolvedLocation from = Resolve(context, step.TargetObjectId, forWrite: false);
                    await EnsureRemoteAsync(from, cancellationToken);
                    BeginRestoreOutput(step, step.Arguments.TargetDirectory!);
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

    /// <summary>
    /// Notes what a restore is about to write. The target must be new or empty, so everything that is
    /// in it afterwards was written by this restore; if it is not empty, nothing is noted and nothing will ever be removed.
    /// </summary>
    private void BeginRestoreOutput(RunStep step, string target)
    {
        string full = Path.GetFullPath(target);
        if (File.Exists(full) || (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()))
        {
            throw new DomainException("restore_target_not_empty", "A snapshot is restored only into a new or empty directory.");
        }

        string? createdRoot = null;
        for (string? path = full; path != null && !Directory.Exists(path); path = Path.GetDirectoryName(path))
        {
            createdRoot = path;
        }

        _restoreOutputs[step.Id] = new RestoreOutput(full, createdRoot);
    }

    /// <summary>
    /// After a step that did not end well: removes the stale locks the stopped programs left in the repositories
    /// it used, and removes what a restore had written, since its files can have their full size and
    /// still miss their content. Nothing that an earlier step did is touched.
    /// </summary>
    private async Task CleanUpAsync(Context context, string runId, RunStep step)
    {
        SecretRedactor redactor = context.Redactor;
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(60));

        foreach (string objectId in RepositoriesUsedBy(step, context))
        {
            try
            {
                ResolvedLocation location = Resolve(context, objectId, forWrite: false);
                await restic.UnlockAsync(Repository(location), limit.Token);
            }
            catch (Exception error) when (error is DomainException or ExternalToolException or OperationCanceledException)
            {
                Log(runId, redactor, $"Stale locks could not be removed from {objectId}: {error.Message}");
            }
        }

        if (!_restoreOutputs.TryRemove(step.Id, out RestoreOutput? output))
        {
            return;
        }

        try
        {
            int removed = 0;
            if (Directory.Exists(output.Target))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(output.Target))
                {
                    if (Directory.Exists(entry) && new DirectoryInfo(entry).LinkTarget == null)
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }

                    removed++;
                }
            }

            if (output.CreatedRoot != null && Directory.Exists(output.CreatedRoot))
            {
                Directory.Delete(output.CreatedRoot, recursive: true);
            }

            if (removed > 0)
            {
                runs.Update(runId, _ => step.Notes.Add("partial_output_removed"));
                Log(runId, redactor, $"Removed what the stopped restore had written into {output.Target}.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            runs.Update(runId, _ => step.Notes.Add("partial_output_left"));
            Log(runId, redactor, $"What the stopped restore had written into {output.Target} could not be removed: {error.Message}");
        }
    }

    private IEnumerable<string> RepositoriesUsedBy(RunStep step, Context context) => step.Kind switch
    {
        StepKind.ResticBackup or StepKind.Forget or StepKind.Prune or StepKind.Check or StepKind.Restore => [step.TargetObjectId],
        StepKind.ResticCopy => [context.SourceObjectId, step.TargetObjectId],
        StepKind.RestoreToDirectory => [context.SourceObjectId],
        _ => [],
    };

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

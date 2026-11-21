using Backup.App.Planning;
using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra;
using Backup.Infra.Logging;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Execution;

/// <summary>
/// Starts the plans that the user confirmed, one run at a time, in the background. Before it starts it checks
/// that the plan is still what the configuration says, and every step looks at its devices again, because a disk
/// can be unplugged between the plan and the run.
/// </summary>
public sealed partial class RunService(
    Workspace workspace,
    RunCatalog runs,
    IDeviceResolver resolver,
    IResticClient restic,
    IRcloneClient rclone,
    IRunLog log,
    TimeProvider time)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (CancellationTokenSource Cancel, Task Done)> _active = [];

    /// <summary>The folders that restores are writing into right now, so that what a stopped restore wrote can be removed.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RestoreOutput> _restoreOutputs = new();

    /// <param name="CreatedRoot">The highest folder that did not exist before the restore; null if the target existed.</param>
    private sealed record RestoreOutput(string Target, string? CreatedRoot);

    public Run Register(Run plan)
    {
        runs.Add(plan);
        return plan;
    }

    /// <summary>Completes when the run is over; at once if it is not going. The state of the run is final by then.</summary>
    public Task WaitAsync(string runId)
    {
        lock (_gate)
        {
            return _active.TryGetValue(runId, out (CancellationTokenSource Cancel, Task Done) active) ? active.Done : Task.CompletedTask;
        }
    }

    /// <exception cref="DomainException">The plan cannot be started: "run_in_progress", "plan_stale", or one of <see cref="Run.EnsureCanStart"/>.</exception>
    public Run Start(string runId, bool acknowledgedDestructive)
    {
        lock (_gate)
        {
            if (runs.Active() != null)
            {
                throw new DomainException("run_in_progress", "Another run is going. Wait for it or cancel it.");
            }

            runs.Read(runId, run =>
            {
                run.EnsureCanStart(time.GetUtcNow(), acknowledgedDestructive);
                EnsurePlanIsCurrent(run);
                return 0;
            });
            runs.Update(runId, run =>
            {
                run.State = RunState.Running;
                run.StartedAt = time.GetUtcNow();
            });

            CancellationTokenSource cancel = new();
            _active[runId] = (cancel, Task.Run(() => ExecuteAsync(runId, cancel.Token), CancellationToken.None));
            return runs.Find(runId)!;
        }
    }

    /// <summary>Asks a run that is going to stop. The program it runs is killed; the run ends as cancelled.</summary>
    public void Cancel(string runId)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(runId, out (CancellationTokenSource Cancel, Task Done) active))
            {
                throw new DomainException("run_not_running", "This run is not going.");
            }

            active.Cancel.Cancel();
        }
    }

    /// <summary>Drops a plan that will not be started.</summary>
    public void Discard(string runId) => runs.Update(runId, run =>
    {
        if (run.State != RunState.Planned)
        {
            throw new DomainException("run_not_planned", "Only a plan that is waiting can be discarded.");
        }

        run.State = RunState.Discarded;
        run.FinishedAt = time.GetUtcNow();
    });

    /// <summary>Stops what is going, so that a clean exit leaves runs cancelled and not interrupted.</summary>
    public async Task ShutdownAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            foreach ((CancellationTokenSource cancel, _) in _active.Values)
            {
                cancel.Cancel();
            }

            tasks = [.. _active.Values.Select(a => a.Done)];
        }

        await Task.WhenAll(tasks);
    }

    private void EnsurePlanIsCurrent(Run run)
    {
        List<string> ids = run.SourceObjectId.Length > 0
            ? [run.SourceObjectId, .. run.Steps.Select(s => s.TargetObjectId)]
            : [run.Steps[0].TargetObjectId];
        (BackupGroup? group, IReadOnlyDictionary<string, StorageDevice> devices) = workspace.Read(
            (devices, groups) => (groups.Find(run.GroupId), (IReadOnlyDictionary<string, StorageDevice>)devices.Devices.ToDictionary(d => d.Id)));
        List<StorageObject> objects = [.. ids.Select(id => group?.Objects.Find(o => o.Id == id)).OfType<StorageObject>()];
        if (group == null || objects.Count != ids.Count || Fingerprint.Of(objects, devices) != run.Fingerprint)
        {
            throw new DomainException("plan_stale", "The configuration changed after this plan was made. Make a new plan.");
        }
    }
}

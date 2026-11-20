using Backup.App.Execution;
using Backup.App.Planning;
using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Logging;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Tests;

public sealed class MemoryRunStore : IRunStore
{
    public List<Run> Saved { get; private set; } = [];

    public List<Run> Load() => Saved;

    public void Save(IReadOnlyList<Run> runs) => Saved = [.. runs];
}

public sealed class SettableTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A planner fixture with a service that runs the plans.</summary>
public sealed class RunServiceFixture : IDisposable
{
    public RunServiceFixture(IResticClient? restic = null)
    {
        Planner = new PlannerFixture();
        Time = new SettableTime(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        Log = new RunLog(Path.Combine(Planner.Root, "logs"));
        Store = new MemoryRunStore();
        Runs = new RunCatalog(Store, Time.Now);
        IResticClient client = restic ?? Planner.Restic;
        PlanMaker = new RunPlanner(Planner.Workspace, new DeviceResolver(Planner.Probe), client, Planner.Rclone, Time);
        Service = new RunService(Planner.Workspace, Runs, new DeviceResolver(Planner.Probe), client, Planner.Rclone, Log, Time);
    }

    public PlannerFixture Planner { get; }

    public SettableTime Time { get; }

    public RunLog Log { get; }

    public MemoryRunStore Store { get; }

    public RunCatalog Runs { get; }

    public RunPlanner PlanMaker { get; }

    public RunService Service { get; }

    public PlannerFixture Fx => Planner;

    public CancellationToken Ct => Planner.Ct;

    public async Task<Run> PlanSync(StorageObject source, params StorageObject[] targets) =>
        Service.Register(await PlanMaker.PlanSyncAsync(Planner.GroupId, source.Id, targets.Length == 0 ? null : [.. targets.Select(t => t.Id)], Ct));

    /// <summary>Starts a plan and waits until the run is over.</summary>
    public async Task<Run> RunToEnd(Run plan, bool acknowledge = true)
    {
        Service.Start(plan.Id, acknowledge);
        await Service.WaitAsync(plan.Id);
        return Runs.Find(plan.Id)!;
    }

    public void Dispose() => Planner.Dispose();
}

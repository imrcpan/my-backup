using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

/// <summary>A real restic, cancelled in the middle of a backup of a few hundred megabytes.</summary>
public sealed class RunCancelRealTests : IDisposable
{
    private readonly RunServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => _fx.Ct;

    [Fact]
    public async Task A_backup_cancelled_in_the_middle_leaves_no_lock_and_the_next_backup_is_whole()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        for (int i = 0; i < 6; i++)
        {
            byte[] bytes = new byte[50_000_000];
            new Random(i).NextBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(source.Path, $"big{i}.bin"), bytes, Ct);
        }

        StorageObject repo = w.Add("local", Path.Combine(w.Root, "repo"), true);
        Run plan = await _fx.PlanSync(source, repo);
        _fx.Service.Start(plan.Id, false);
        for (int i = 0; i < 200 && _fx.Runs.Read(plan.Id, r => r.Steps[0].Progress) == 0 && _fx.Runs.Find(plan.Id)!.State == RunState.Running; i++)
        {
            await Task.Delay(50, Ct);
        }

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        Run cancelled = _fx.Runs.Find(plan.Id)!;
        Assert.Equal(RunState.Cancelled, cancelled.State);
        Assert.Equal(StepState.Cancelled, cancelled.Steps[0].State);
        string locks = Path.Combine(repo.Path, "locks");
        Assert.Empty(Directory.Exists(locks) ? Directory.GetFiles(locks) : []);
        Assert.Empty(await w.Restic.SnapshotsAsync(w.RepoOf(repo), Ct));

        // The same backup again, as the user would start it: it must be whole and must read back the same.
        Run again = await _fx.RunToEnd(await _fx.PlanSync(source, repo));
        Assert.Equal(RunState.Succeeded, again.State);
        Assert.True((await w.Restic.CheckAsync(w.RepoOf(repo), ResticCheckMode.Full, 10, null, Ct)).Ok);

        string target = Path.Combine(w.Root, "restored");
        string snapshot = (await w.Restic.SnapshotsAsync(w.RepoOf(repo), Ct)).Single().Id;
        await w.Restic.RestoreAsync(w.RepoOf(repo), snapshot, target, null, null, Ct);
        for (int i = 0; i < 6; i++)
        {
            string name = $"big{i}.bin";
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(source.Path, name), Ct),
                await File.ReadAllBytesAsync(Path.Combine(target, source.Path.TrimStart('/'), name), Ct));
        }
    }

    [Fact]
    public async Task A_file_sync_cancelled_in_the_middle_is_completed_by_the_next_sync_and_leaves_no_half_files()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        for (int i = 0; i < 6; i++)
        {
            byte[] bytes = new byte[50_000_000];
            new Random(100 + i).NextBytes(bytes);
            await File.WriteAllBytesAsync(Path.Combine(source.Path, $"big{i}.bin"), bytes, Ct);
        }

        w.Add("disk", "/copy", false);
        Run plan = await _fx.PlanSync(source);
        _fx.Service.Start(plan.Id, true);
        string target = Path.Combine(w.Mount, "copy");
        for (int i = 0; i < 400 && !(Directory.Exists(target) && Directory.GetFiles(target, "big*").Length > 0) && _fx.Runs.Find(plan.Id)!.State == RunState.Running; i++)
        {
            await Task.Delay(25, Ct);
        }

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        // A file is only ever under its real name when it is whole.
        foreach (string file in Directory.GetFiles(target, "big*.bin"))
        {
            Assert.Equal(50_000_000, new FileInfo(file).Length);
        }

        Run again = await _fx.RunToEnd(await _fx.PlanSync(source));
        Assert.Equal(RunState.Succeeded, again.State);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(source.Path, $"big{i}.bin"), Ct),
                await File.ReadAllBytesAsync(Path.Combine(target, $"big{i}.bin"), Ct));
        }

        Assert.Empty(Directory.GetFiles(target, "*.partial"));
        Assert.Equal(StepState.UpToDate, (await _fx.PlanSync(source)).Steps[0].State);
    }
}

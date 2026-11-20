using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Disks;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

public sealed class RunServiceTests : IDisposable
{
    private readonly RunServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => _fx.Ct;

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public async Task A_group_is_brought_up_to_date_from_its_newest_copy_and_then_is_up_to_date()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        string diskDir = Path.Combine(w.Mount, "media");
        Directory.CreateDirectory(diskDir);
        File.WriteAllText(Path.Combine(diskDir, "stale.txt"), "not in the newest copy");
        StorageObject disk = w.Add("disk", "/media", false);
        StorageObject cloud = w.Add("gdrive", Path.Combine(w.Cloud, "repo"), true);
        StorageObject local = w.Add("local", Path.Combine(w.Root, "local-repo"), true);

        Run run = await _fx.RunToEnd(await _fx.PlanSync(source));

        Assert.Equal(RunState.Succeeded, run.State);
        Assert.All(run.Steps, s => Assert.Equal(StepState.Succeeded, s.State));
        Assert.NotNull(run.FinishedAt);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(diskDir, "a.txt")));
        Assert.Equal("world!", File.ReadAllText(Path.Combine(diskDir, "sub/b.txt")));
        Assert.False(File.Exists(Path.Combine(diskDir, "stale.txt")));
        Assert.Single(await w.Restic.SnapshotsAsync(w.RepoOf(cloud), Ct));
        Assert.Single(await w.Restic.SnapshotsAsync(w.RepoOf(local), Ct));
        Assert.True(Directory.Exists(source.Path) && File.Exists(Path.Combine(source.Path, "a.txt")), "the source is never touched");

        Run again = await _fx.PlanSync(source);
        Assert.All(again.Steps, s => Assert.Equal(StepState.UpToDate, s.State));
        Assert.Contains(disk.Id, again.Steps.Select(s => s.TargetObjectId));
    }

    [Fact]
    public async Task The_log_tells_what_happened_and_has_no_password()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject cloud = w.Add("gdrive", Path.Combine(w.Cloud, "repo"), true);

        Run run = await _fx.RunToEnd(await _fx.PlanSync(source));

        IReadOnlyList<string> log = _fx.Log.Tail(run.Id, 100);
        Assert.Contains(log, l => l.Contains("Run started"));
        Assert.Contains(log, l => l.Contains("Run ended: Succeeded"));
        Assert.Contains(log, l => l.Contains("Snapshot "));
        Assert.DoesNotContain(cloud.Password!, string.Join('\n', log));
    }

    [Fact]
    public async Task Encrypted_to_encrypted_and_encrypted_to_plain_are_carried_out()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject a = w.Add("gdrive", Path.Combine(w.Cloud, "a"), true);
        await w.Restic.InitAsync(w.RepoOf(a), Ct);
        await w.Restic.BackupAsync(w.RepoOf(a), source.Path, null, Ct);
        StorageObject b = w.Add("local", Path.Combine(w.Root, "b-repo"), true);
        StorageObject plain = w.Add("disk", "/restored", false);

        Run run = await _fx.RunToEnd(await _fx.PlanSync(a, b, plain));

        Assert.Equal(RunState.Succeeded, run.State);
        Assert.Equal([StepKind.ResticCopy, StepKind.RestoreToDirectory], run.Steps.Select(s => s.Kind));
        Assert.Single(await w.Restic.SnapshotsAsync(w.RepoOf(b), Ct));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(w.Mount, "restored/a.txt")));
        Assert.Equal("world!", File.ReadAllText(Path.Combine(w.Mount, "restored/sub/b.txt")));
    }

    [Fact]
    public async Task A_disk_unplugged_after_the_plan_fails_that_step_only()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        string diskDir = Path.Combine(w.Mount, "media");
        Directory.CreateDirectory(diskDir);
        File.WriteAllText(Path.Combine(diskDir, "keep.txt"), "k");
        w.Add("disk", "/media", false);
        StorageObject cloud = w.Add("gdrive", Path.Combine(w.Cloud, "repo"), true);
        Run plan = await _fx.PlanSync(source);
        Assert.All(plan.Steps, s => Assert.Equal(StepState.Pending, s.State));

        w.Probe.Status = new DiskStatus(DiskState.NotConnected);
        Run run = await _fx.RunToEnd(plan);

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal(StepState.Failed, run.Steps[0].State);
        Assert.Equal("disk_not_connected", run.Steps[0].ErrorCode);
        Assert.Equal(StepState.Succeeded, run.Steps[1].State);
        Assert.Equal(["keep.txt"], Directory.GetFiles(diskDir).Select(Path.GetFileName));
        Assert.Single(await w.Restic.SnapshotsAsync(w.RepoOf(cloud), Ct));
    }

    [Fact]
    public async Task A_source_that_became_empty_after_the_plan_is_never_backed_up()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject cloud = w.Add("gdrive", Path.Combine(w.Cloud, "repo"), true);
        Run plan = await _fx.PlanSync(source);

        foreach (string entry in Directory.GetFileSystemEntries(source.Path))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Run run = await _fx.RunToEnd(plan);

        Assert.Equal("source_empty", run.Steps[0].ErrorCode);
        Assert.False(Directory.Exists(cloud.Path), "no repository is made for an empty source");
    }

    [Fact]
    public async Task A_sync_never_deletes_more_than_the_plan_showed()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        string diskDir = Path.Combine(w.Mount, "media");
        Directory.CreateDirectory(diskDir);
        File.WriteAllText(Path.Combine(diskDir, "stale1.txt"), "1");
        w.Add("disk", "/media", false);
        Run plan = await _fx.PlanSync(source);
        Assert.Equal(1, plan.Steps[0].ToDelete);

        File.WriteAllText(Path.Combine(diskDir, "stale2.txt"), "2");
        File.WriteAllText(Path.Combine(diskDir, "stale3.txt"), "3");
        Run run = await _fx.RunToEnd(plan);

        Assert.Equal(StepState.Failed, run.Steps[0].State);
        Assert.True(Directory.GetFiles(diskDir, "stale*").Length >= 2, "at most one stale file may go");
    }

    [Fact]
    public async Task Forget_prune_restore_and_check_are_carried_out()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject repo = w.Add("gdrive", Path.Combine(w.Cloud, "repo"), true);
        await w.Restic.InitAsync(w.RepoOf(repo), Ct);
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(source.Path, "a.txt"), $"version {i}");
            await w.Restic.BackupAsync(w.RepoOf(repo), source.Path, null, Ct);
        }

        string[] ids = [.. (await w.Restic.SnapshotsAsync(w.RepoOf(repo), Ct)).Select(s => s.Id)];
        Run forget = _fx.Service.Register(await _fx.PlanMaker.PlanForgetAsync(w.GroupId, repo.Id, [ids[0], ids[1]], true, Ct));
        Assert.Equal("run_acknowledge_required", CodeOf(() => _fx.Service.Start(forget.Id, false)));
        Assert.Equal(3, (await w.Restic.SnapshotsAsync(w.RepoOf(repo), Ct)).Count);
        Assert.Equal(RunState.Succeeded, (await _fx.RunToEnd(forget, acknowledge: true)).State);
        Assert.Equal([ids[2]], (await w.Restic.SnapshotsAsync(w.RepoOf(repo), Ct)).Select(s => s.Id));

        Run check = _fx.Service.Register(await _fx.PlanMaker.PlanCheckAsync(w.GroupId, repo.Id, CheckDepth.Full, 10, Ct));
        Assert.Equal(RunState.Succeeded, (await _fx.RunToEnd(check, acknowledge: false)).State);

        string target = Path.Combine(w.Root, "restored");
        Run restore = _fx.Service.Register(await _fx.PlanMaker.PlanRestoreAsync(w.GroupId, repo.Id, ids[2], target, null, Ct));
        Assert.Equal(RunState.Succeeded, (await _fx.RunToEnd(restore, acknowledge: false)).State);
        Assert.Equal("version 2", File.ReadAllText(Path.Combine(target, source.Path.TrimStart('/'), "a.txt")));

        Run prune = _fx.Service.Register(await _fx.PlanMaker.PlanPruneAsync(w.GroupId, repo.Id, Ct));
        Assert.Equal("run_acknowledge_required", CodeOf(() => _fx.Service.Start(prune.Id, false)));
        Assert.Equal(RunState.Succeeded, (await _fx.RunToEnd(prune)).State);
    }

    [Fact]
    public async Task A_check_that_finds_errors_fails_the_run_with_the_output()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        for (int i = 0; i < 20; i++)
        {
            File.WriteAllText(Path.Combine(source.Path, $"f{i}.txt"), new string((char)('a' + i), 5000 + i));
        }

        StorageObject repo = w.Add("local", Path.Combine(w.Root, "repo"), true);
        await w.Restic.InitAsync(w.RepoOf(repo), Ct);
        await w.Restic.BackupAsync(w.RepoOf(repo), source.Path, null, Ct);
        string pack = Directory.EnumerateFiles(Path.Combine(repo.Path, "data"), "*", SearchOption.AllDirectories).First();
        byte[] bytes = File.ReadAllBytes(pack);
        for (int i = 10; i < bytes.Length - 10; i += 7)
        {
            bytes[i] ^= 0xFF;
        }

        File.WriteAllBytes(pack, bytes);

        Run check = _fx.Service.Register(await _fx.PlanMaker.PlanCheckAsync(w.GroupId, repo.Id, CheckDepth.Full, 10, Ct));
        Run run = await _fx.RunToEnd(check, acknowledge: false);

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal("check_failed", run.Steps[0].ErrorCode);
    }

    [Fact]
    public async Task A_plan_is_started_only_if_it_is_current_recent_and_alone()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject target = w.Add("local", Path.Combine(w.Root, "repo"), true);

        Run stale = await _fx.PlanSync(source);
        w.Workspace.UpdateObject(w.GroupId, target.Id, "local", Path.Combine(w.Root, "other"), true, "");
        Assert.Equal("plan_stale", CodeOf(() => _fx.Service.Start(stale.Id, true)));
        Assert.Equal(RunState.Planned, _fx.Runs.Find(stale.Id)!.State);

        Run old = await _fx.PlanSync(source);
        _fx.Time.Now += Run.PlanLifetime + TimeSpan.FromMinutes(1);
        Assert.Equal("run_expired", CodeOf(() => _fx.Service.Start(old.Id, true)));
        Assert.Equal("run_not_found", CodeOf(() => _fx.Service.Start("nope", true)));

        Run fresh = await _fx.PlanSync(source);
        _fx.Service.Discard(fresh.Id);
        Assert.Equal("run_not_planned", CodeOf(() => _fx.Service.Start(fresh.Id, true)));
        Assert.Equal("run_not_planned", CodeOf(() => _fx.Service.Discard(fresh.Id)));
    }

    [Fact]
    public async Task A_run_that_is_not_going_cannot_be_cancelled()
    {
        PlannerFixture w = _fx.Planner;
        w.LocalSource();
        Assert.Equal("run_not_running", CodeOf(() => _fx.Service.Cancel("nope")));
        await Task.CompletedTask;
    }
}

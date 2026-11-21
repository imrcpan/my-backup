using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

/// <summary>
/// What a cancelled or failed run leaves behind. What earlier steps finished stays; what the step that
/// was going had only half done does not stay, because it could be taken for the real thing.
/// </summary>
public sealed class RunCancelTests : IDisposable
{
    private const string SnapshotId = "aaaaaaaa11111111aaaaaaaa11111111aaaaaaaa11111111aaaaaaaa11111111";

    private readonly FakeRestic _restic = new();
    private readonly RunServiceFixture _fx;
    private readonly StorageObject _repo;

    public RunCancelTests()
    {
        _fx = new RunServiceFixture(_restic);
        _repo = _fx.Planner.Add("local", Path.Combine(_fx.Planner.Root, "repo"), true);
        _restic.Snapshots.Add(new ResticSnapshot(SnapshotId, SnapshotId[..8], DateTimeOffset.UtcNow, "tree", ["/data"], "host", null, null));
    }

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => _fx.Ct;

    private string Target(string relative) => Path.Combine(_fx.Planner.Root, "restore", relative);

    private async Task<Run> PlanRestore(string target) =>
        _fx.Service.Register(await _fx.PlanMaker.PlanRestoreAsync(_fx.Planner.GroupId, _repo.Id, SnapshotId, target, null, Ct));

    /// <summary>A restore that writes a file of the right size but not its content, tells that it did, and then waits.</summary>
    private TaskCompletionSource HalfWayRestore()
    {
        TaskCompletionSource writing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _restic.OnRestore = async (_, target, ct) =>
        {
            Directory.CreateDirectory(Path.Combine(target, "data/sub"));
            File.WriteAllBytes(Path.Combine(target, "data/sub/movie.mkv"), new byte[1_000_000]);
            writing.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ResticRestoreSummary(0, 0, 0, 0);
        };
        return writing;
    }

    [Fact]
    public async Task A_cancelled_restore_removes_what_it_had_written_including_the_folders_it_made()
    {
        TaskCompletionSource writing = HalfWayRestore();
        Run plan = await PlanRestore(Target("a/b"));
        _fx.Service.Start(plan.Id, false);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(File.Exists(Path.Combine(Target("a/b"), "data/sub/movie.mkv")));

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        Run run = _fx.Runs.Find(plan.Id)!;
        Assert.Equal(RunState.Cancelled, run.State);
        Assert.Contains("partial_output_removed", run.Steps[0].Notes);
        Assert.False(Directory.Exists(Target("")), "the folders that the restore made are gone, up to the first one that did not exist");
        Assert.Contains(_repo.Path, _restic.Unlocked);
    }

    [Fact]
    public async Task A_folder_that_existed_empty_is_kept_but_emptied()
    {
        Directory.CreateDirectory(Target(""));
        TaskCompletionSource writing = HalfWayRestore();
        Run plan = await PlanRestore(Target(""));
        _fx.Service.Start(plan.Id, false);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        Assert.True(Directory.Exists(Target("")));
        Assert.Empty(Directory.GetFileSystemEntries(Target("")));
    }

    [Fact]
    public async Task A_failed_restore_is_cleaned_in_the_same_way()
    {
        _restic.OnRestore = (_, target, _) =>
        {
            Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, "half.bin"), new byte[100]);
            throw new ExternalToolException("restic_failed", "connection lost");
        };
        Run plan = await PlanRestore(Target("x"));

        Run run = await _fx.RunToEnd(plan, acknowledge: false);

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal("restic_failed", run.Steps[0].ErrorCode);
        Assert.False(Directory.Exists(Target("")));
    }

    [Fact]
    public async Task A_restore_that_ends_well_keeps_what_it_wrote()
    {
        _restic.OnRestore = (_, target, _) =>
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "done.txt"), "whole");
            return Task.FromResult(new ResticRestoreSummary(1, 1, 5, 5));
        };

        Run run = await _fx.RunToEnd(await PlanRestore(Target("ok")), acknowledge: false);

        Assert.Equal(RunState.Succeeded, run.State);
        Assert.Equal("whole", File.ReadAllText(Path.Combine(Target("ok"), "done.txt")));
        Assert.DoesNotContain("partial_output_removed", run.Steps[0].Notes);
    }

    [Fact]
    public async Task Files_that_the_user_put_into_the_folder_after_the_plan_are_never_removed()
    {
        _restic.OnRestore = (_, _, _) => throw new InvalidOperationException("must not be reached");
        Run plan = await PlanRestore(Target("later"));
        Directory.CreateDirectory(Target("later"));
        string mine = Path.Combine(Target("later"), "mine.txt");
        File.WriteAllText(mine, "keep me");

        Run run = await _fx.RunToEnd(plan, acknowledge: false);

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal("restore_target_not_empty", run.Steps[0].ErrorCode);
        Assert.Equal("keep me", File.ReadAllText(mine));
        Assert.DoesNotContain("partial_output_removed", run.Steps[0].Notes);
    }

    [Fact]
    public async Task Steps_that_were_finished_stay_finished_when_a_later_step_is_cancelled()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        // The files of the disk are copied by a step that ends; the backup after it is cancelled.
        StorageObject disk = w.Add("disk", "/copy", false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _restic.OnBackup = async (_, _, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        Run plan = await _fx.PlanSync(source, disk, _repo);
        _fx.Service.Start(plan.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        Run run = _fx.Runs.Find(plan.Id)!;
        Assert.Equal(RunState.Cancelled, run.State);
        Assert.Equal([StepState.Succeeded, StepState.Cancelled], run.Steps.Select(s => s.State));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(w.Mount, "copy/a.txt")));
        Assert.Equal("world!", File.ReadAllText(Path.Combine(w.Mount, "copy/sub/b.txt")));
        Assert.Equal([_repo.Path], _restic.Unlocked);
        Assert.True(File.Exists(Path.Combine(source.Path, "a.txt")), "the source is never touched");
    }
}

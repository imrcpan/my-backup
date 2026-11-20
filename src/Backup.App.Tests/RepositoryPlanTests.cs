using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

public sealed class RepositoryPlanTests : IDisposable
{
    private readonly PlannerFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private async Task<(StorageObject Repo, string[] SnapshotIds)> RepoWithSnapshots(int count)
    {
        StorageObject source = _fx.LocalSource();
        StorageObject repo = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(repo), _fx.Ct);
        for (int i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(source.Path, "a.txt"), $"version {i}");
            await _fx.Restic.BackupAsync(_fx.RepoOf(repo), source.Path, null, _fx.Ct);
        }

        return (repo, [.. (await _fx.Restic.SnapshotsAsync(_fx.RepoOf(repo), _fx.Ct)).Select(s => s.Id)]);
    }

    [Fact]
    public async Task Restore_plan_names_the_snapshot_and_a_new_target()
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(2);
        string target = Path.Combine(_fx.Root, "out");

        Run run = await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, ids[0][..8], target, null, _fx.Ct);

        Assert.Equal(RunKind.Restore, run.Kind);
        RunStep step = Assert.Single(run.Steps);
        Assert.Equal(StepState.Pending, step.State);
        Assert.Equal(ids[0], step.Arguments.SnapshotId);
        Assert.Equal(target, step.Arguments.TargetDirectory);
        Assert.False(Directory.Exists(target), "planning must not create the target");
        Assert.False(run.Destructive);
    }

    [Fact]
    public async Task Restore_into_a_directory_with_files_is_blocked()
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(1);
        string target = Directory.CreateDirectory(Path.Combine(_fx.Root, "busy")).FullName;
        File.WriteAllText(Path.Combine(target, "mine.txt"), "x");

        RunStep step = Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, ids[0], target, null, _fx.Ct)).Steps);

        Assert.Equal("restore_target_not_empty", step.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/dir")]
    [InlineData(null)]
    public async Task Restore_target_must_be_an_absolute_path(string? target)
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(1);
        RunStep step = Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, ids[0], target, null, _fx.Ct)).Steps);
        Assert.Equal("restore_target_invalid", step.ErrorCode);
    }

    [Fact]
    public async Task Restore_cannot_write_into_the_repository_itself()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject repo = _fx.Add("local", Path.Combine(_fx.Root, "repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(repo), _fx.Ct);
        string id = (await _fx.Restic.BackupAsync(_fx.RepoOf(repo), source.Path, null, _fx.Ct)).SnapshotId;

        RunStep step = Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, id, Path.Combine(repo.Path, "data/x"), null, _fx.Ct)).Steps);

        Assert.Equal("restore_target_in_repository", step.ErrorCode);
    }

    [Fact]
    public async Task Restore_of_chosen_paths_lists_them_and_refuses_odd_ones()
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(1);
        string target = Path.Combine(_fx.Root, "out");

        RunStep ok = Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, ids[0], target, ["/a/b/", "/a/b", "/c"], _fx.Ct)).Steps);
        Assert.Equal(["/a/b", "/c"], ok.Arguments.Paths);
        Assert.Equal(["/a/b", "/c"], ok.Lines.Select(l => l.Path));

        RunStep bad = Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, ids[0], target, ["/a/../etc"], _fx.Ct)).Steps);
        Assert.Equal("path_invalid", bad.ErrorCode);
    }

    [Fact]
    public async Task Restore_needs_a_snapshot_that_exists_exactly()
    {
        (StorageObject repo, _) = await RepoWithSnapshots(1);
        string target = Path.Combine(_fx.Root, "out");
        Assert.Equal("snapshot_not_found", Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, "deadbeef", target, null, _fx.Ct)).Steps).ErrorCode);
        Assert.Equal("snapshot_not_found", Assert.Single((await _fx.Planner.PlanRestoreAsync(_fx.GroupId, repo.Id, "", target, null, _fx.Ct)).Steps).ErrorCode);
    }

    [Fact]
    public async Task Forget_by_policy_lists_what_goes_and_what_stays_and_removes_nothing()
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(4);
        _fx.Workspace.Write((_, groups) => groups.SetRetention(_fx.GroupId, repo.Id, new RetentionPolicy(KeepLast: 1)));

        Run run = await _fx.Planner.PlanForgetAsync(_fx.GroupId, repo.Id, null, true, _fx.Ct);

        RunStep step = Assert.Single(run.Steps);
        Assert.Equal(3, step.ToDelete);
        Assert.Equal(ids[..3], step.Arguments.SnapshotIds);
        Assert.Equal(3, step.Lines.Count(l => l.Action == "remove"));
        Assert.Equal(1, step.Lines.Count(l => l.Action == "keep"));
        Assert.Contains("prunes_after", step.Notes);
        Assert.True(run.Destructive);
        Assert.Equal(4, (await _fx.Restic.SnapshotsAsync(_fx.RepoOf(repo), _fx.Ct)).Count);
    }

    [Fact]
    public async Task Forget_by_policy_needs_a_policy_and_with_nothing_to_remove_is_up_to_date()
    {
        (StorageObject repo, _) = await RepoWithSnapshots(2);
        Assert.Equal("retention_not_set", Assert.Single((await _fx.Planner.PlanForgetAsync(_fx.GroupId, repo.Id, null, false, _fx.Ct)).Steps).ErrorCode);

        _fx.Workspace.Write((_, groups) => groups.SetRetention(_fx.GroupId, repo.Id, new RetentionPolicy(KeepLast: 10)));
        Run run = await _fx.Planner.PlanForgetAsync(_fx.GroupId, repo.Id, null, false, _fx.Ct);
        Assert.Equal(StepState.UpToDate, run.Steps[0].State);
        Assert.False(run.Destructive);
    }

    [Fact]
    public async Task Forget_of_chosen_snapshots_removes_exactly_those()
    {
        (StorageObject repo, string[] ids) = await RepoWithSnapshots(3);

        RunStep step = Assert.Single((await _fx.Planner.PlanForgetAsync(_fx.GroupId, repo.Id, [ids[1][..8]], false, _fx.Ct)).Steps);

        Assert.Equal([ids[1]], step.Arguments.SnapshotIds);
        Assert.Null(step.Arguments.Retention);
        Assert.Equal(1, step.ToDelete);

        RunStep unknown = Assert.Single((await _fx.Planner.PlanForgetAsync(_fx.GroupId, repo.Id, ["deadbeef"], false, _fx.Ct)).Steps);
        Assert.Equal(StepState.Blocked, unknown.State);
        Assert.Equal("snapshot_not_found", unknown.ErrorCode);
    }

    [Fact]
    public async Task Prune_and_check_plans_need_an_existing_repository_and_mark_what_they_do()
    {
        StorageObject repo = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "none"), true);
        Assert.Equal("repo_missing", Assert.Single((await _fx.Planner.PlanPruneAsync(_fx.GroupId, repo.Id, _fx.Ct)).Steps).ErrorCode);
        Assert.Equal("repo_missing", Assert.Single((await _fx.Planner.PlanCheckAsync(_fx.GroupId, repo.Id, CheckDepth.Structure, 10, _fx.Ct)).Steps).ErrorCode);

        await _fx.Restic.InitAsync(_fx.RepoOf(repo), _fx.Ct);
        Run prune = await _fx.Planner.PlanPruneAsync(_fx.GroupId, repo.Id, _fx.Ct);
        Assert.Contains("irreversible", prune.Steps[0].Notes);
        Assert.True(prune.Destructive);

        Run full = await _fx.Planner.PlanCheckAsync(_fx.GroupId, repo.Id, CheckDepth.Full, 10, _fx.Ct);
        Assert.Contains("reads_all_data", full.Steps[0].Notes);
        Assert.False(full.Destructive);
        Assert.Equal("check_subset_invalid", Assert.Single((await _fx.Planner.PlanCheckAsync(_fx.GroupId, repo.Id, CheckDepth.Subset, 0, _fx.Ct)).Steps).ErrorCode);
    }

    [Fact]
    public async Task A_plain_object_is_not_a_repository()
    {
        StorageObject plain = _fx.LocalSource();
        Assert.Equal("object_not_a_repository", Assert.Single((await _fx.Planner.PlanPruneAsync(_fx.GroupId, plain.Id, _fx.Ct)).Steps).ErrorCode);
        await Assert.ThrowsAsync<DomainException>(() => _fx.Planner.PlanPruneAsync(_fx.GroupId, "nope", _fx.Ct));
    }
}

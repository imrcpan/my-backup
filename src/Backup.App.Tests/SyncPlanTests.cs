using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Disks;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

public sealed class SyncPlanTests : IDisposable
{
    private readonly PlannerFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private Task<Run> Plan(StorageObject source, params StorageObject[] targets) =>
        _fx.Planner.PlanSyncAsync(_fx.GroupId, source.Id, targets.Length == 0 ? null : [.. targets.Select(t => t.Id)], _fx.Ct);

    [Fact]
    public async Task Plain_to_plain_lists_the_files_to_copy_and_to_delete_and_changes_nothing()
    {
        StorageObject source = _fx.LocalSource();
        string targetDir = Path.Combine(_fx.Mount, "media");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "stale.txt"), "only here");
        StorageObject target = _fx.Add("disk", "/media/", false);

        Run run = await Plan(source);

        Assert.Equal(RunState.Planned, run.State);
        Assert.Equal(source.Id, run.SourceObjectId);
        RunStep step = Assert.Single(run.Steps);
        Assert.Equal(StepKind.FileSync, step.Kind);
        Assert.Equal(target.Id, step.TargetObjectId);
        Assert.Equal(StepState.Pending, step.State);
        Assert.Equal(2, step.ToAdd);
        Assert.Equal(1, step.ToDelete);
        Assert.Equal(11, step.Bytes);
        Assert.Contains("deletes_in_target", step.Notes);
        Assert.Equal(["copy", "copy", "delete"], step.Lines.Select(l => l.Action).Order());
        Assert.True(run.Destructive);
        Assert.True(File.Exists(Path.Combine(targetDir, "stale.txt")));
        Assert.False(File.Exists(Path.Combine(targetDir, "a.txt")));
    }

    [Fact]
    public async Task A_target_that_is_current_is_up_to_date()
    {
        StorageObject source = _fx.LocalSource();
        string targetDir = Path.Combine(_fx.Mount, "media");
        await _fx.Rclone.SyncAsync(source.Path, targetDir, 0, null, _fx.Ct);
        _fx.Add("disk", "/media", false);

        RunStep step = Assert.Single((await Plan(source)).Steps);

        Assert.Equal(StepState.UpToDate, step.State);
        Assert.False(step.Destructive);
    }

    [Fact]
    public async Task Plain_to_a_new_encrypted_cloud_repository_counts_the_source_and_creates_nothing()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject target = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);

        RunStep step = Assert.Single((await Plan(source)).Steps);

        Assert.Equal(StepKind.ResticBackup, step.Kind);
        Assert.Equal(StepState.Pending, step.State);
        Assert.Contains("creates_repository", step.Notes);
        Assert.Equal(2, step.ToAdd);
        Assert.Equal(11, step.Bytes);
        Assert.All(step.Lines, l => Assert.Equal("new", l.Action));
        Assert.False(Directory.Exists(target.Path), "planning must not create the repository");
    }

    [Fact]
    public async Task Plain_to_an_existing_repository_shows_what_changed_since_the_last_backup()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject target = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(target), _fx.Ct);
        await _fx.Restic.BackupAsync(_fx.RepoOf(target), source.Path, null, _fx.Ct);

        Assert.Equal(StepState.UpToDate, Assert.Single((await Plan(source)).Steps).State);

        File.WriteAllText(Path.Combine(source.Path, "c.txt"), "new file");
        RunStep step = Assert.Single((await Plan(source)).Steps);
        Assert.Equal(StepState.Pending, step.State);
        Assert.Equal(1, step.ToAdd);
        Assert.DoesNotContain("creates_repository", step.Notes);
        Assert.Equal([Path.Combine(source.Path, "c.txt")], step.Lines.Select(l => l.Path));
    }

    [Fact]
    public async Task Encrypted_to_encrypted_copies_the_latest_snapshot_until_it_is_there()
    {
        StorageObject plain = _fx.LocalSource();
        StorageObject a = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "a"), true);
        StorageObject b = _fx.Add("local", Path.Combine(_fx.Root, "b-repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(a), _fx.Ct);
        await _fx.Restic.BackupAsync(_fx.RepoOf(a), plain.Path, null, _fx.Ct);
        ResticSnapshot latest = (await _fx.Restic.SnapshotsAsync(_fx.RepoOf(a), _fx.Ct)).Single();

        RunStep step = Assert.Single((await Plan(a, b)).Steps);
        Assert.Equal(StepKind.ResticCopy, step.Kind);
        Assert.Contains("creates_repository", step.Notes);
        Assert.Equal(latest.Id, step.Arguments.SnapshotId);
        Assert.Equal("copy", Assert.Single(step.Lines).Action);

        await _fx.Restic.InitFromAsync(_fx.RepoOf(b), _fx.RepoOf(a), _fx.Ct);
        await _fx.Restic.CopyAsync(_fx.RepoOf(b), _fx.RepoOf(a), latest.Id, null, _fx.Ct);
        Assert.Equal(StepState.UpToDate, Assert.Single((await Plan(a, b)).Steps).State);
    }

    [Fact]
    public async Task Encrypted_to_plain_restores_into_an_empty_directory_only()
    {
        StorageObject plain = _fx.LocalSource();
        StorageObject repo = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(repo), _fx.Ct);
        await _fx.Restic.BackupAsync(_fx.RepoOf(repo), plain.Path, null, _fx.Ct);
        string targetDir = Path.Combine(_fx.Mount, "restored");
        StorageObject target = _fx.Add("disk", "/restored", false);

        RunStep step = Assert.Single((await Plan(repo, target)).Steps);
        Assert.Equal(StepKind.RestoreToDirectory, step.Kind);
        Assert.Equal(StepState.Pending, step.State);
        Assert.Equal(plain.Path, step.Arguments.SnapshotPath);

        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "mine.txt"), "do not overwrite");
        RunStep blocked = Assert.Single((await Plan(repo, target)).Steps);
        Assert.Equal(StepState.Blocked, blocked.State);
        Assert.Equal("restore_target_not_empty", blocked.ErrorCode);
    }

    [Fact]
    public async Task All_other_objects_are_targets_and_one_blocked_step_does_not_block_the_others()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject disk = _fx.Add("disk", "/media", false);
        StorageObject cloud = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        _fx.Probe.Status = new DiskStatus(DiskState.NotConnected);

        Run run = await Plan(source);

        Assert.Equal([disk.Id, cloud.Id], run.Steps.Select(s => s.TargetObjectId));
        Assert.Equal(StepState.Blocked, run.Steps[0].State);
        Assert.Equal("disk_not_connected", run.Steps[0].ErrorCode);
        Assert.Equal(StepState.Pending, run.Steps[1].State);
        Assert.Throws<DomainException>(() => run.EnsureCanStart(DateTimeOffset.UtcNow, true));
    }

    [Fact]
    public async Task An_empty_source_directory_is_blocked_so_that_an_unmounted_disk_is_never_backed_up()
    {
        string mounted = Path.Combine(_fx.Mount, "media");
        Directory.CreateDirectory(mounted);
        StorageObject source = _fx.Add("disk", "/media", false);
        _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);

        RunStep step = Assert.Single((await Plan(source)).Steps);

        Assert.Equal(StepState.Blocked, step.State);
        Assert.Equal("source_empty", step.ErrorCode);
    }

    [Fact]
    public async Task A_source_on_a_disk_that_is_not_mounted_is_blocked()
    {
        StorageObject source = _fx.Add("disk", "/media", false);
        _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        _fx.Probe.Status = new DiskStatus(DiskState.NotMounted, "/dev/fake1");

        RunStep step = Assert.Single((await Plan(source)).Steps);

        Assert.Equal("disk_not_mounted", step.ErrorCode);
    }

    [Fact]
    public async Task A_wrong_repository_password_is_blocked_without_showing_it()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject target = _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        await _fx.Restic.InitAsync(_fx.RepoOf(target) with { Password = "another-password-1" }, _fx.Ct);

        RunStep step = Assert.Single((await Plan(source)).Steps);

        Assert.Equal("repo_wrong_password", step.ErrorCode);
        Assert.DoesNotContain(target.Password!, step.ErrorMessage);
    }

    [Fact]
    public async Task A_new_repository_is_not_made_inside_a_directory_with_other_files()
    {
        StorageObject source = _fx.LocalSource();
        string target = Path.Combine(_fx.Root, "busy");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "photo.jpg"), "x");
        _fx.Add("local", target, true);

        Assert.Equal("target_not_empty", Assert.Single((await Plan(source)).Steps).ErrorCode);
    }

    [Fact]
    public async Task A_cloud_remote_that_is_not_configured_is_blocked()
    {
        StorageObject source = _fx.LocalSource();
        _fx.Add("gdrive", Path.Combine(_fx.Cloud, "repo"), true);
        File.WriteAllText(_fx.RcloneConfig, "[other]\ntype = local\n");

        Assert.Equal("remote_not_configured", Assert.Single((await Plan(source)).Steps).ErrorCode);
    }

    [Fact]
    public async Task Objects_that_lie_inside_each_other_on_disk_are_blocked_even_across_devices()
    {
        StorageObject source = _fx.Add("local", Path.Combine(_fx.Root, "mount"), false);
        File.WriteAllText(Path.Combine(_fx.Mount, "f.txt"), "x");
        _fx.Add("disk", "/backup", false);

        Assert.Equal("sync_overlap", Assert.Single((await Plan(source)).Steps).ErrorCode);
    }

    [Fact]
    public async Task The_fingerprint_changes_when_the_configuration_changes()
    {
        StorageObject source = _fx.LocalSource();
        StorageObject target = _fx.Add("local", Path.Combine(_fx.Root, "t"), true);
        string before = (await Plan(source)).Fingerprint;
        Assert.Equal(before, (await Plan(source)).Fingerprint);

        _fx.Workspace.UpdateObject(_fx.GroupId, target.Id, "local", Path.Combine(_fx.Root, "t2"), true, "");
        Assert.NotEqual(before, (await Plan(source)).Fingerprint);
    }

    [Fact]
    public async Task Choosing_no_valid_target_is_refused_before_any_step()
    {
        StorageObject source = _fx.LocalSource();
        await Assert.ThrowsAsync<DomainException>(() => _fx.Planner.PlanSyncAsync(_fx.GroupId, source.Id, null, _fx.Ct));
        await Assert.ThrowsAsync<DomainException>(() => _fx.Planner.PlanSyncAsync(_fx.GroupId, null, null, _fx.Ct));
        await Assert.ThrowsAsync<DomainException>(() => _fx.Planner.PlanSyncAsync("nope", source.Id, null, _fx.Ct));
    }
}

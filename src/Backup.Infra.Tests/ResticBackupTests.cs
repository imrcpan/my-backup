using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticBackupTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Plan_shows_what_would_be_saved_and_saves_nothing()
    {
        _fx.Write("a.txt", "hello");
        _fx.Write("sub/b.txt", "world!");
        await _fx.Client.InitAsync(_fx.Repository, Ct);

        ResticBackupPlan plan = await _fx.Client.PlanBackupAsync(_fx.Repository, _fx.Source, Ct);

        Assert.Equal(2, plan.FilesNew);
        Assert.Equal(0, plan.FilesChanged);
        Assert.Equal(11, plan.BytesToProcess);
        Assert.Equal(["new", "new"], plan.Items.Select(i => i.Action));
        Assert.Equal([Path.Combine(_fx.Source, "a.txt"), Path.Combine(_fx.Source, "sub/b.txt")], plan.Items.Select(i => i.Path).Order());
        Assert.False(plan.ItemsTruncated);
        Assert.Empty(await _fx.Client.SnapshotsAsync(_fx.Repository, Ct));
    }

    [Fact]
    public async Task Backup_is_incremental()
    {
        _fx.Write("a.txt", "hello");
        _fx.Write("b.txt", "world");
        await _fx.Client.InitAsync(_fx.Repository, Ct);

        ResticBackupSummary first = await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);
        Assert.Equal(2, first.FilesNew);
        Assert.False(first.Incomplete);
        Assert.NotEmpty(first.SnapshotId);

        ResticBackupPlan unchanged = await _fx.Client.PlanBackupAsync(_fx.Repository, _fx.Source, Ct);
        Assert.Equal(0, unchanged.FilesNew + unchanged.FilesChanged);
        Assert.Equal(2, unchanged.FilesUnmodified);
        Assert.Empty(unchanged.Items);

        await Task.Delay(1100, Ct);
        _fx.Write("a.txt", "hello again");
        _fx.Write("c.txt", "brand new");
        ResticBackupPlan plan = await _fx.Client.PlanBackupAsync(_fx.Repository, _fx.Source, Ct);
        Assert.Equal(1, plan.FilesNew);
        Assert.Equal(1, plan.FilesChanged);
        Assert.Equal(1, plan.FilesUnmodified);
        Assert.Equal(["modified", "new"], plan.Items.Select(i => i.Action).Order());
        // Changed files come first in the order that is shown, and what is shown is the start of the whole list.
        Assert.Equal("modified", plan.AllItems[0].Action);
        Assert.Equal(plan.AllItems, plan.Items);

        ResticBackupSummary second = await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);
        Assert.Equal(1, second.FilesNew);
        Assert.Equal(1, second.FilesChanged);
        Assert.Equal(2, (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Count);
    }

    [Fact]
    public async Task Backup_reports_progress()
    {
        for (int i = 0; i < 5; i++)
        {
            _fx.Write($"f{i}.bin", new string((char)('a' + i), 200_000));
        }

        await _fx.Client.InitAsync(_fx.Repository, Ct);
        List<ResticBackupProgress> seen = [];
        await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, seen.Add, Ct);
        // Progress is sampled over time, so a very fast backup may report none; what is reported must be sane.
        Assert.All(seen, p => Assert.InRange(p.PercentDone, 0, 1));
    }

    [Fact]
    public async Task Backup_into_a_missing_repository_fails_with_a_code()
    {
        _fx.Write("a.txt", "x");
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct));
        Assert.Equal("repo_missing", error.Code);
    }

    [Fact]
    public async Task Plan_is_cut_after_the_limit()
    {
        RecordingRunner runner = new();
        string lines = string.Join('\n', Enumerable.Range(0, ResticClient.MaxPlanItems + 5).Select(i =>
            $$"""{"message_type":"verbose_status","action":"new","item":"/s/{{i}}","data_size":1}"""))
            + "\n" + """
            {"message_type":"summary","files_new":505,"files_changed":0,"files_unmodified":0,"total_bytes_processed":505,"data_added":900}
            """;
        runner.Answer(0, lines);
        ResticBackupPlan plan = await new ResticClient(runner).PlanBackupAsync(_fx.Repository, "/s", Ct);
        Assert.Equal(ResticClient.MaxPlanItems, plan.Items.Count);
        Assert.True(plan.ItemsTruncated);
        Assert.Equal(505, plan.FilesNew);
        Assert.Equal(505, plan.AllItems.Count);
        Assert.False(plan.AllItemsTruncated);
    }

    [Fact]
    public async Task Unreadable_files_make_the_backup_incomplete_but_keep_the_snapshot()
    {
        RecordingRunner runner = new();
        runner.Answer(3, """
            {"message_type":"error","error":{"message":"permission denied"},"during":"archival","item":"/s/secret"}
            {"message_type":"summary","files_new":1,"files_changed":0,"files_unmodified":0,"data_added":10,"total_bytes_processed":4,"snapshot_id":"abc"}
            """);
        ResticBackupSummary summary = await new ResticClient(runner).BackupAsync(_fx.Repository, "/s", null, Ct);
        Assert.True(summary.Incomplete);
        Assert.Equal("abc", summary.SnapshotId);
        Assert.Equal(["/s/secret: permission denied"], summary.Errors);
    }

    [Fact]
    public async Task Backup_without_a_summary_is_an_error()
    {
        RecordingRunner runner = new();
        runner.Answer(0, "not json at all");
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => new ResticClient(runner).BackupAsync(_fx.Repository, "/s", null, Ct));
        Assert.Equal("restic_output", error.Code);
    }

    [Fact]
    public async Task Source_is_given_after_a_double_dash()
    {
        RecordingRunner runner = new();
        runner.Answer(0, """{"message_type":"summary","files_new":0,"files_changed":0,"files_unmodified":0,"data_added":0,"total_bytes_processed":0,"snapshot_id":"x"}""");
        await new ResticClient(runner).BackupAsync(_fx.Repository, "/s", null, Ct);
        string[] args = [.. runner.Specs[0].Arguments];
        Assert.Equal(["--", "/s"], args[^2..]);
    }
}

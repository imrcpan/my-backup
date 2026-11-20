using Backup.Core;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticForgetTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<ResticSnapshot>> FourSnapshots()
    {
        await _fx.Client.InitAsync(_fx.Repository, Ct);
        for (int i = 0; i < 4; i++)
        {
            _fx.Write("a.txt", $"version {i}");
            await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);
        }

        return [.. await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)];
    }

    [Fact]
    public async Task Plan_names_what_goes_and_changes_nothing()
    {
        List<ResticSnapshot> all = await FourSnapshots();

        ResticForgetPlan plan = await _fx.Client.PlanForgetAsync(_fx.Repository, new RetentionPolicy(KeepLast: 2), Ct);

        Assert.Equal(all.Take(2).Select(s => s.Id), plan.Remove.Select(s => s.Id));
        Assert.Equal(all.Skip(2).Select(s => s.Id), plan.Keep.Select(s => s.Id));
        Assert.Equal(4, (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Count);
    }

    [Fact]
    public async Task Forget_removes_exactly_what_the_plan_said()
    {
        List<ResticSnapshot> all = await FourSnapshots();
        RetentionPolicy policy = new(KeepLast: 1);
        ResticForgetPlan plan = await _fx.Client.PlanForgetAsync(_fx.Repository, policy, Ct);

        ResticForgetPlan done = await _fx.Client.ForgetAsync(_fx.Repository, policy, Ct);

        Assert.Equal(plan.Remove.Select(s => s.Id), done.Remove.Select(s => s.Id));
        Assert.Equal([all[3].Id], (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Select(s => s.Id));
    }

    [Fact]
    public async Task A_policy_that_keeps_nothing_never_reaches_restic()
    {
        RecordingRunner runner = new();
        ResticClient client = new(runner);
        DomainException error = await Assert.ThrowsAsync<DomainException>(() => client.ForgetAsync(_fx.Repository, new RetentionPolicy(), Ct));
        Assert.Equal("retention_empty", error.Code);
        Assert.Empty(runner.Specs);
    }

    [Fact]
    public async Task Policy_becomes_keep_flags_for_the_numbers_that_are_set()
    {
        RecordingRunner runner = new();
        runner.Answer(0, "[]");
        await new ResticClient(runner).PlanForgetAsync(_fx.Repository, new RetentionPolicy(KeepLast: 3, KeepMonthly: 12), Ct);
        Assert.Equal(["forget", "--json", "--dry-run", "--keep-last", "3", "--keep-monthly", "12"], runner.Specs[0].Arguments.Skip(2));
    }

    [Fact]
    public async Task Chosen_snapshots_are_planned_then_forgotten()
    {
        List<ResticSnapshot> all = await FourSnapshots();

        ResticForgetPlan plan = await _fx.Client.PlanForgetSnapshotsAsync(_fx.Repository, [all[1].ShortId, all[2].Id], Ct);
        Assert.Equal([all[1].Id, all[2].Id], plan.Remove.Select(s => s.Id));
        Assert.Equal(4, (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Count);

        await _fx.Client.ForgetSnapshotsAsync(_fx.Repository, [all[1].ShortId, all[2].Id], Ct);
        Assert.Equal([all[0].Id, all[3].Id], (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Select(s => s.Id));
    }

    [Fact]
    public async Task One_bad_id_removes_nothing()
    {
        List<ResticSnapshot> all = await FourSnapshots();
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.ForgetSnapshotsAsync(_fx.Repository, [all[0].ShortId, "deadbeef"], Ct));
        Assert.Equal("snapshot_not_found", error.Code);
        Assert.Equal(4, (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Count);
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("-r")]
    [InlineData("abc")]
    [InlineData("zzzzzzzz")]
    [InlineData("")]
    public async Task Ids_that_could_be_taken_for_options_are_refused_before_restic_runs(string id)
    {
        RecordingRunner runner = new();
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => new ResticClient(runner).ForgetSnapshotsAsync(_fx.Repository, [id], Ct));
        Assert.Equal("snapshot_id_invalid", error.Code);
        Assert.Empty(runner.Specs);
    }

    [Fact]
    public async Task No_ids_is_refused()
    {
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => new ResticClient(new RecordingRunner()).ForgetSnapshotsAsync(_fx.Repository, [], Ct));
        Assert.Equal("snapshot_id_invalid", error.Code);
    }

    [Fact]
    public async Task Prune_after_forget_keeps_the_remaining_snapshot_restorable()
    {
        List<ResticSnapshot> all = await FourSnapshots();
        await _fx.Client.ForgetAsync(_fx.Repository, new RetentionPolicy(KeepLast: 1), Ct);
        List<string> lines = [];
        await _fx.Client.PruneAsync(_fx.Repository, lines.Add, Ct);
        Assert.NotEmpty(lines);

        string target = Path.Combine(_fx.Root, "out");
        await _fx.Client.RestoreAsync(_fx.Repository, all[3].Id, target, null, null, Ct);
        Assert.Equal("version 3", File.ReadAllText(Path.Combine(target, _fx.Source.TrimStart('/'), "a.txt")));
    }
}

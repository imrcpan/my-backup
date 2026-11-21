using Backup.Infra.Process;
using Backup.Infra.Rclone;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class RcloneSyncTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rclone-sync").FullName;
    private readonly string _a;
    private readonly string _b;
    private readonly RcloneClient _client;

    public RcloneSyncTests()
    {
        _a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        _b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        string config = Path.Combine(_root, "rclone.conf");
        File.WriteAllText(config, "[disk]\ntype = local\n");
        _client = new RcloneClient(new ProcessRunner(), new RcloneOptions(ConfigFile: config));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Put(string dir, string relative, string content)
    {
        string path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task Plan_lists_copies_and_deletes_and_changes_nothing()
    {
        Put(_a, "x.txt", "hello");
        Put(_a, "sub/y.txt", "world!");
        Put(_a, "chg.txt", "changed content");
        Put(_b, "chg.txt", "old");
        Put(_b, "extra.txt", "only in target");

        RcloneSyncPlan plan = await _client.PlanSyncAsync(_a, _b, Ct);

        Assert.Equal(3, plan.CopyCount);
        Assert.Equal(1, plan.DeleteCount);
        Assert.Equal(5 + 6 + 15, plan.BytesToCopy);
        Assert.Equal(["delete:extra.txt"], plan.Items.Where(i => i.Action == "delete").Select(i => $"{i.Action}:{i.Path}"));
        Assert.Equal(["chg.txt", "sub/y.txt", "x.txt"], plan.Items.Where(i => i.Action == "copy").Select(i => i.Path).Order());
        Assert.False(plan.ItemsTruncated);
        Assert.True(File.Exists(Path.Combine(_b, "extra.txt")));
        Assert.False(File.Exists(Path.Combine(_b, "x.txt")));
    }

    [Fact]
    public async Task Sync_makes_the_target_like_the_source_and_never_touches_the_source()
    {
        Put(_a, "x.txt", "hello");
        Put(_a, "sub/y.txt", "world!");
        Put(_b, "extra.txt", "only in target");
        RcloneSyncPlan plan = await _client.PlanSyncAsync(_a, _b, Ct);

        await _client.SyncAsync(_a, _b, plan.DeleteCount, null, Ct);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(_b, "x.txt")));
        Assert.Equal("world!", File.ReadAllText(Path.Combine(_b, "sub/y.txt")));
        Assert.False(File.Exists(Path.Combine(_b, "extra.txt")));
        Assert.Equal(["sub", "x.txt"], Directory.GetFileSystemEntries(_a).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Sync_refuses_to_delete_more_than_was_confirmed()
    {
        Put(_a, "keep.txt", "k");
        Put(_b, "d1.txt", "1");
        Put(_b, "d2.txt", "2");
        Put(_b, "d3.txt", "3");

        await Assert.ThrowsAsync<ExternalToolException>(() => _client.SyncAsync(_a, _b, 1, null, Ct));

        Assert.True(Directory.GetFiles(_b).Length >= 2, "at most one file may be deleted");
    }

    [Fact]
    public async Task A_source_that_changed_to_empty_deletes_nothing_when_no_deletes_were_confirmed()
    {
        Put(_b, "precious.txt", "p");
        await Assert.ThrowsAsync<ExternalToolException>(() => _client.SyncAsync(_a, _b, 0, null, Ct));
        Assert.True(File.Exists(Path.Combine(_b, "precious.txt")));
    }

    [Fact]
    public async Task Remote_paths_work_too()
    {
        Put(_a, "x.txt", "hello");
        RcloneSyncPlan plan = await _client.PlanSyncAsync(_a, $"disk:{_b}", Ct);
        Assert.Equal(1, plan.CopyCount);
    }

    [Fact]
    public async Task Plan_is_cut_after_the_limit_but_counts_everything()
    {
        RecordingRunner runner = new();
        runner.Answer(0, "", string.Join('\n', Enumerable.Range(0, RcloneClient.MaxPlanItems + 3).Select(i =>
            $$"""{"level":"notice","msg":"Skipped copy","skipped":"copy","size":2,"object":"f{{i}}"}""")));
        RcloneSyncPlan plan = await new RcloneClient(runner).PlanSyncAsync("/a", "/b", Ct);
        Assert.Equal(RcloneClient.MaxPlanItems, plan.Items.Count);
        Assert.Equal(RcloneClient.MaxPlanItems + 3, plan.CopyCount);
        Assert.True(plan.ItemsTruncated);
    }

    [Fact]
    public async Task Progress_is_read_from_the_stats_lines()
    {
        RecordingRunner runner = new();
        runner.Answer(0, "", """{"level":"notice","msg":"stats","stats":{"bytes":50,"totalBytes":200,"transfers":1,"totalTransfers":4}}""");
        List<RcloneSyncProgress> seen = [];
        await new RcloneClient(runner).SyncAsync("/a", "/b", 0, seen.Add, Ct);
        Assert.Equal([new RcloneSyncProgress(50, 200, 1, 4)], seen);
    }

    [Theory]
    [InlineData("--delete-excluded", "/b")]
    [InlineData("/a", "-x")]
    [InlineData("relative/path", "/b")]
    [InlineData("a b:/x", "/b")]
    public async Task Locations_that_could_be_options_never_reach_rclone(string source, string target)
    {
        RecordingRunner runner = new();
        RcloneClient client = new(runner);
        await Assert.ThrowsAsync<ExternalToolException>(() => client.PlanSyncAsync(source, target, Ct));
        await Assert.ThrowsAsync<ExternalToolException>(() => client.SyncAsync(source, target, 0, null, Ct));
        Assert.Empty(runner.Specs);
    }

    [Fact]
    public async Task Max_delete_and_the_end_of_options_are_always_passed()
    {
        RecordingRunner runner = new();
        await new RcloneClient(runner).SyncAsync("/a", "/b", 7, null, Ct);
        string[] args = [.. runner.Specs[0].Arguments];
        Assert.Equal(["--max-delete", "7"], args.SkipWhile(a => a != "--max-delete").Take(2));
        Assert.Equal(["--", "/a", "/b"], args[^3..]);
    }

    [Fact]
    public async Task The_deletions_come_first_in_the_plan_and_the_whole_list_is_kept_with_the_bytes_of_each_kind()
    {
        Put(_a, "zeta.txt", "123456");
        Put(_a, "alpha/one.txt", "12");
        Put(_a, "beta.txt", "12345678");
        Put(_b, "stale-z.txt", "123");
        Put(_b, "stale-a.txt", "1234567");

        RcloneSyncPlan plan = await _client.PlanSyncAsync(_a, _b, Ct);

        Assert.Equal(["delete:stale-a.txt", "delete:stale-z.txt", "copy:alpha/one.txt", "copy:beta.txt", "copy:zeta.txt"],
            plan.Items.Select(i => $"{i.Action}:{i.Path}"));
        Assert.Equal(plan.Items, plan.AllItems);
        Assert.False(plan.ItemsTruncated);
        Assert.False(plan.AllItemsTruncated);
        Assert.Equal(2 + 8 + 6, plan.BytesToCopy);
        Assert.Equal(3 + 7, plan.BytesToDelete);
        Assert.Equal(2, plan.DeleteCount);
        Assert.Equal(3, plan.CopyCount);
    }

    [Fact]
    public async Task When_the_list_is_cut_the_deletions_are_what_stays_and_the_whole_list_is_longer()
    {
        RecordingRunner runner = new();
        List<string> lines = [];
        for (int i = 0; i < RcloneClient.MaxPlanItems + 40; i++)
        {
            lines.Add($$"""{"skipped":"copy","size":1,"object":"c{{i:D5}}"}""");
        }

        lines.Add("""{"skipped":"delete","size":9,"object":"gone.txt"}""");
        runner.Answer(0, "", string.Join('\n', lines));

        RcloneSyncPlan plan = await new RcloneClient(runner).PlanSyncAsync("/a", "/b", Ct);

        Assert.Equal("delete", plan.Items[0].Action);
        Assert.Equal("gone.txt", plan.Items[0].Path);
        Assert.Equal(RcloneClient.MaxPlanItems, plan.Items.Count);
        Assert.True(plan.ItemsTruncated);
        Assert.Equal(RcloneClient.MaxPlanItems + 41, plan.AllItems.Count);
        Assert.False(plan.AllItemsTruncated);
        Assert.Equal(9, plan.BytesToDelete);
    }
}

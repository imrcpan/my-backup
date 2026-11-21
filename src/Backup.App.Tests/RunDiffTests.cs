using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Persistence;
using Xunit;

namespace Backup.App.Tests;

public sealed class RunDiffTests : IDisposable
{
    private readonly RunServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static string[] Lines(Stream? stream)
    {
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToArray();
    }

    private async Task<(Run Run, StorageObject Target)> BigSync(int extraFiles, string[] staleNames)
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        for (int i = 0; i < extraFiles; i++)
        {
            File.WriteAllText(Path.Combine(source.Path, $"f{i:D4}.txt"), "x");
        }

        string target = Directory.CreateDirectory(Path.Combine(w.Mount, "copy")).FullName;
        foreach (string name in staleNames)
        {
            File.WriteAllText(Path.Combine(target, name), new string('s', 10));
        }

        StorageObject disk = w.Add("disk", "/copy", false);
        return (await _fx.PlanSync(source, disk), disk);
    }

    [Fact]
    public async Task The_deleted_files_are_the_first_lines_and_the_whole_list_is_longer_than_what_is_shown()
    {
        (Run run, _) = await BigSync(600, ["zzz-old.txt", "aaa-old.txt", "mmm-old.txt"]);
        RunStep step = Assert.Single(run.Steps);

        Assert.Equal(500, step.Lines.Count);
        Assert.True(step.LinesTruncated);
        Assert.Equal(["delete", "delete", "delete", "copy"], step.Lines.Take(4).Select(l => l.Action));
        Assert.EndsWith("aaa-old.txt", step.Lines[0].Path);
        Assert.EndsWith("mmm-old.txt", step.Lines[1].Path);
        Assert.Equal(3, step.ToDelete);
        Assert.Equal(30, step.BytesToDelete);
        Assert.Equal(602, step.ToAdd);

        Assert.True(step.HasDiff);
        Assert.Null(step.CompleteDiff);
        string[] all = Lines(_fx.Diffs.Open(run.Id, step.Id));
        Assert.Equal(3 + 602, all.Length);
        Assert.All(all.Take(3), l => Assert.StartsWith("-\t10\t", l));
        Assert.All(all.Skip(3), l => Assert.StartsWith("+\t", l));
        Assert.Equal(step.Lines.Select(l => l.Path), all.Take(500).Select(l => l.Split('\t')[2]));
    }

    [Fact]
    public async Task A_plan_that_is_small_has_a_diff_too_so_the_button_is_always_the_same()
    {
        (Run run, _) = await BigSync(0, ["old.txt"]);
        RunStep step = Assert.Single(run.Steps);

        Assert.False(step.LinesTruncated);
        Assert.True(step.HasDiff);
        string[] all = Lines(_fx.Diffs.Open(run.Id, step.Id));
        Assert.Equal(3, all.Length);
        // rclone names the files below the folder that is synced.
        Assert.Equal("-\t10\told.txt", all[0]);
    }

    [Fact]
    public async Task A_step_with_nothing_to_do_has_no_diff()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        await w.Rclone.SyncAsync(source.Path, Path.Combine(w.Mount, "copy"), 0, null, _fx.Ct);
        StorageObject disk = w.Add("disk", "/copy", false);

        Run run = await _fx.PlanSync(source, disk);

        Assert.Equal(StepState.UpToDate, run.Steps[0].State);
        Assert.False(run.Steps[0].HasDiff);
        Assert.Null(_fx.Diffs.Open(run.Id, run.Steps[0].Id));
    }

    [Fact]
    public async Task The_files_of_plans_that_are_not_in_the_history_any_more_are_removed_when_a_plan_is_registered()
    {
        string stray = Path.Combine(_fx.Planner.Root, "plans", "arunthatisgone");
        Directory.CreateDirectory(stray);
        File.WriteAllText(Path.Combine(stray, "s1.diff.txt"), "old");

        (Run run, _) = await BigSync(0, ["x.txt"]);

        Assert.False(Directory.Exists(stray));
        Assert.NotNull(_fx.Diffs.Open(run.Id, run.Steps[0].Id));
    }

    private sealed class FailingStore : IPlanDiffStore
    {
        public void Write(string runId, string stepId, string header, IEnumerable<PlanLine> lines) => throw new IOException("No space left on device");

        public Stream? Open(string runId, string stepId) => null;

        public void Keep(IReadOnlyCollection<string> runIds)
        {
        }
    }

    [Fact]
    public async Task A_disk_that_cannot_take_the_diff_does_not_make_the_plan_fail()
    {
        PlannerFixture w = _fx.Planner;
        StorageObject source = w.LocalSource();
        StorageObject disk = w.Add("disk", "/copy", false);
        Backup.App.Execution.RunService service = new(w.Workspace, _fx.Runs, new Backup.Infra.Storage.DeviceResolver(w.Probe), w.Restic, w.Rclone, _fx.Log, new FailingStore(), _fx.Time);

        Run plan = await _fx.PlanMaker.PlanSyncAsync(w.GroupId, source.Id, [disk.Id], _fx.Ct);
        Run registered = service.Register(plan);

        RunStep step = Assert.Single(registered.Steps);
        Assert.Equal(StepState.Pending, step.State);
        Assert.False(step.HasDiff);
        Assert.NotEmpty(step.Lines);
    }
}

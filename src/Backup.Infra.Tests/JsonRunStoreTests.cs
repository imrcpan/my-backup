using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Persistence;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class JsonRunStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("runs-test").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string RunsPath => Path.Combine(_dir, "runs.json");

    [Fact]
    public void Nothing_saved_gives_no_runs()
    {
        Assert.Empty(new JsonRunStore(RunsPath).Load());
    }

    [Fact]
    public void A_run_with_steps_round_trips()
    {
        Run run = new()
        {
            Id = "r1",
            Kind = RunKind.Sync,
            GroupId = "g",
            GroupName = "Media",
            SourceObjectId = "o1",
            Fingerprint = "fp",
            State = RunState.Planned,
            CreatedAt = new DateTimeOffset(2026, 11, 20, 1, 2, 3, TimeSpan.FromHours(8)),
            Steps =
            [
                new RunStep
                {
                    Id = "s1", Kind = StepKind.ResticBackup, TargetObjectId = "o2", State = StepState.Pending,
                    Lines = [new PlanLine { Action = "new", Path = "/a/b ünï.txt", Size = 12 }], ToAdd = 1, Bytes = 12, Notes = ["creates_repository"],
                },
            ],
        };

        new JsonRunStore(RunsPath).Save([run]);
        Run back = Assert.Single(new JsonRunStore(RunsPath).Load());

        Assert.Equal(run.CreatedAt, back.CreatedAt);
        Assert.Equal(RunKind.Sync, back.Kind);
        RunStep step = Assert.Single(back.Steps);
        Assert.Equal(StepKind.ResticBackup, step.Kind);
        Assert.Equal("/a/b ünï.txt", step.Lines.Single().Path);
        Assert.Equal(["creates_repository"], step.Notes);
        Assert.Contains("\"Planned\"", File.ReadAllText(RunsPath));
    }

    [Fact]
    public void A_corrupt_history_is_reported_and_left_alone()
    {
        File.WriteAllText(RunsPath, "[{ broken");
        Assert.Equal("state_corrupt", Assert.Throws<DomainException>(() => new JsonRunStore(RunsPath).Load()).Code);
        Assert.Equal("[{ broken", File.ReadAllText(RunsPath));
    }
}

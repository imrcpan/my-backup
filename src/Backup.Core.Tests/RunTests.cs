using Backup.Core.Runs;
using Xunit;

namespace Backup.Core.Tests;

public class RunTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemoryRunStore(List<Run>? initial = null) : IRunStore
    {
        public List<Run> Saved { get; private set; } = initial ?? [];

        public int Saves { get; private set; }

        public List<Run> Load() => Saved;

        public void Save(IReadOnlyList<Run> runs)
        {
            Saves++;
            Saved = [.. runs];
        }
    }

    private static Run Planned(StepKind kind = StepKind.FileSync, StepState state = StepState.Pending, long toDelete = 0, DateTimeOffset? at = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = RunKind.Sync,
        State = RunState.Planned,
        CreatedAt = at ?? Now,
        Steps = [new RunStep { Id = "s1", Kind = kind, State = state, ToDelete = toDelete }],
    };

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void A_fresh_plan_can_be_started()
    {
        Planned().EnsureCanStart(Now.AddMinutes(1), false);
    }

    [Fact]
    public void A_plan_can_be_started_only_once_and_only_while_it_is_a_plan()
    {
        Run run = Planned();
        run.State = RunState.Running;
        Assert.Equal("run_not_planned", CodeOf(() => run.EnsureCanStart(Now, false)));
        run.State = RunState.Discarded;
        Assert.Equal("run_not_planned", CodeOf(() => run.EnsureCanStart(Now, false)));
    }

    [Fact]
    public void An_old_plan_has_expired()
    {
        Run run = Planned();
        run.EnsureCanStart(Now + Run.PlanLifetime, false);
        Assert.Equal("run_expired", CodeOf(() => run.EnsureCanStart(Now + Run.PlanLifetime + TimeSpan.FromSeconds(1), false)));
    }

    [Fact]
    public void A_plan_with_a_blocked_step_or_no_step_cannot_be_started()
    {
        Assert.Equal("run_blocked", CodeOf(() => Planned(state: StepState.Blocked).EnsureCanStart(Now, true)));
        Run empty = Planned();
        empty.Steps.Clear();
        Assert.Equal("run_blocked", CodeOf(() => empty.EnsureCanStart(Now, true)));
    }

    [Theory]
    [InlineData(StepKind.FileSync, 3L, true)]
    [InlineData(StepKind.FileSync, 0L, false)]
    [InlineData(StepKind.Forget, 0L, true)]
    [InlineData(StepKind.Prune, 0L, true)]
    [InlineData(StepKind.ResticBackup, 0L, false)]
    [InlineData(StepKind.Restore, 0L, false)]
    public void Only_runs_that_delete_need_an_explicit_acknowledgement(StepKind kind, long toDelete, bool destructive)
    {
        Run run = Planned(kind, toDelete: toDelete);
        Assert.Equal(destructive, run.Destructive);
        if (destructive)
        {
            Assert.Equal("run_acknowledge_required", CodeOf(() => run.EnsureCanStart(Now, false)));
        }
        else
        {
            run.EnsureCanStart(Now, false);
        }

        run.EnsureCanStart(Now, true);
    }

    [Fact]
    public void After_a_restart_a_running_run_is_interrupted_with_its_steps()
    {
        Run run = new()
        {
            Id = "r",
            State = RunState.Running,
            CreatedAt = Now,
            Steps = [new() { State = StepState.Succeeded }, new() { State = StepState.Running }, new() { State = StepState.Pending }],
        };
        MemoryRunStore store = new([run]);

        RunCatalog catalog = new(store, Now.AddMinutes(5));

        Run back = catalog.Find("r")!;
        Assert.Equal(RunState.Interrupted, back.State);
        Assert.Equal(Now.AddMinutes(5), back.FinishedAt);
        Assert.Equal([StepState.Succeeded, StepState.Interrupted, StepState.Skipped], back.Steps.Select(s => s.State));
        Assert.Null(catalog.Active());
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void After_a_restart_old_plans_are_discarded_and_new_ones_stay()
    {
        Run old = Planned(at: Now);
        Run fresh = Planned(at: Now.AddMinutes(40));
        RunCatalog catalog = new(new MemoryRunStore([old, fresh]), Now.AddMinutes(45));
        Assert.Equal(RunState.Discarded, catalog.Find(old.Id)!.State);
        Assert.Equal(RunState.Planned, catalog.Find(fresh.Id)!.State);
    }

    [Fact]
    public void Nothing_is_saved_at_start_if_nothing_changed()
    {
        MemoryRunStore store = new([new Run { Id = "done", State = RunState.Succeeded, CreatedAt = Now }]);
        _ = new RunCatalog(store, Now);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Update_saves_and_touch_does_not()
    {
        MemoryRunStore store = new();
        RunCatalog catalog = new(store, Now);
        Run run = Planned();
        catalog.Add(run);
        int saves = store.Saves;

        catalog.Touch(run.Id, r => r.Steps[0].Progress = 0.5);
        Assert.Equal(saves, store.Saves);
        catalog.Update(run.Id, r => r.State = RunState.Running);
        Assert.Equal(saves + 1, store.Saves);
        Assert.Equal(run.Id, catalog.Active()!.Id);
        Assert.Equal("run_not_found", CodeOf(() => catalog.Update("nope", _ => { })));
    }

    [Fact]
    public void Only_the_newest_finished_runs_are_kept_and_unfinished_ones_never_dropped()
    {
        RunCatalog catalog = new(new MemoryRunStore(), Now);
        // A run that is going is never dropped, however old it is (a plan that waits is another matter: it expires).
        Run waiting = Planned(at: Now.AddDays(-100));
        waiting.State = RunState.Running;
        catalog.Add(waiting);
        for (int i = 0; i < RunCatalog.KeepFinished + 5; i++)
        {
            catalog.Add(new Run { Id = $"f{i}", State = RunState.Succeeded, CreatedAt = Now.AddMinutes(i) });
        }

        Assert.Equal(RunCatalog.KeepFinished + 1, catalog.List().Count);
        Assert.NotNull(catalog.Find(waiting.Id));
        Assert.Null(catalog.Find("f0"));
        Assert.NotNull(catalog.Find($"f{RunCatalog.KeepFinished + 4}"));
    }

    [Fact]
    public void List_shows_the_newest_first()
    {
        RunCatalog catalog = new(new MemoryRunStore(), Now);
        catalog.Add(new Run { Id = "a", State = RunState.Succeeded, CreatedAt = Now });
        catalog.Add(new Run { Id = "b", State = RunState.Succeeded, CreatedAt = Now.AddHours(1) });
        Assert.Equal(["b", "a"], catalog.List().Select(r => r.Id));
    }

    [Fact]
    public async Task A_run_that_is_read_while_it_changes_is_a_consistent_copy_and_never_throws()
    {
        RunCatalog catalog = new(new MemoryRunStore(), Now);
        Run run = Planned(StepKind.ResticBackup);
        run.State = RunState.Running;
        catalog.Add(run);
        using CancellationTokenSource stop = new();

        // The executor changes the lists of the run while the interface reads and serializes it.
        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < 20_000; i++)
            {
                catalog.Update(run.Id, r =>
                {
                    r.Steps[0].Notes.Add("n" + i);
                    r.Steps[0].Lines.Add(new PlanLine { Action = "new", Path = "/p" + i });
                    if (r.Steps[0].Notes.Count > 50)
                    {
                        r.Steps[0].Notes.Clear();
                        r.Steps[0].Lines.Clear();
                    }
                });
            }

            stop.Cancel();
        }, TestContext.Current.CancellationToken);
        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (Run copy in catalog.List())
                {
                    Assert.Equal(copy.Steps[0].Notes.Count, copy.Steps[0].Notes.Count);
                    foreach (string note in copy.Steps[0].Notes)
                    {
                        _ = note.Length;
                    }

                    foreach (PlanLine line in catalog.Find(run.Id)!.Steps[0].Lines)
                    {
                        _ = line.Path.Length;
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public void What_Find_and_List_return_is_a_copy_that_does_not_change_the_catalog()
    {
        RunCatalog catalog = new(new MemoryRunStore(), Now);
        Run run = Planned();
        catalog.Add(run);

        Run copy = catalog.Find(run.Id)!;
        copy.State = RunState.Failed;
        copy.Steps[0].Notes.Add("tampered");
        copy.Steps[0].Arguments.Paths.Add("/x");
        catalog.List()[0].Steps.Clear();

        Run fresh = catalog.Find(run.Id)!;
        Assert.Equal(RunState.Planned, fresh.State);
        Assert.Empty(fresh.Steps[0].Notes);
        Assert.Empty(fresh.Steps[0].Arguments.Paths);
        Assert.Single(fresh.Steps);
    }

    [Fact]
    public void A_plan_that_nobody_confirmed_in_time_is_discarded_when_the_next_run_is_added_and_a_recent_one_is_not()
    {
        RunCatalog catalog = new(new MemoryRunStore(), Now);
        Run stale = Planned(at: Now);
        Run recent = Planned(at: Now.AddMinutes(20));
        catalog.Add(stale);
        catalog.Add(recent);
        Assert.Equal(RunState.Planned, catalog.Find(stale.Id)!.State);

        catalog.Add(Planned(at: Now.AddMinutes(45)));

        Run discarded = catalog.Find(stale.Id)!;
        Assert.Equal(RunState.Discarded, discarded.State);
        Assert.Equal(Now.AddMinutes(45), discarded.FinishedAt);
        Assert.Equal(RunState.Planned, catalog.Find(recent.Id)!.State);
        Assert.Equal("run_not_planned", CodeOf(() => discarded.EnsureCanStart(Now.AddMinutes(45), true)));
    }
}

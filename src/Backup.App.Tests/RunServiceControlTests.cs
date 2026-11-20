using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.App.Tests;

public sealed class RunServiceControlTests : IDisposable
{
    private readonly FakeRestic _restic = new();
    private readonly RunServiceFixture _fx;
    private readonly StorageObject _source;
    private readonly StorageObject _first;
    private readonly StorageObject _second;

    public RunServiceControlTests()
    {
        _fx = new RunServiceFixture(_restic);
        _source = _fx.Planner.LocalSource();
        _first = _fx.Planner.Add("local", Path.Combine(_fx.Planner.Root, "repo1"), true);
        _second = _fx.Planner.Add("local", Path.Combine(_fx.Planner.Root, "repo2"), true);
    }

    public void Dispose() => _fx.Dispose();

    private static ResticBackupSummary Summary(bool incomplete = false, params string[] errors) =>
        new("0123456789abcdef", 1, 0, 0, 5, 5, incomplete, errors);

    /// <summary>A backup that tells when it has begun and goes on only when it is let go, or ends when it is cancelled.</summary>
    private (TaskCompletionSource Started, TaskCompletionSource Go) Hold()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _restic.OnBackup = async (_, _, _, ct) =>
        {
            started.TrySetResult();
            await go.Task.WaitAsync(ct);
            return Summary();
        };
        return (started, go);
    }

    [Fact]
    public async Task Cancel_stops_the_running_step_and_skips_the_rest()
    {
        (TaskCompletionSource started, _) = Hold();
        Run plan = await _fx.PlanSync(_source);
        _fx.Service.Start(plan.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), _fx.Ct);
        Assert.Equal(RunState.Running, _fx.Runs.Find(plan.Id)!.State);

        _fx.Service.Cancel(plan.Id);
        await _fx.Service.WaitAsync(plan.Id);

        Run run = _fx.Runs.Find(plan.Id)!;
        Assert.Equal(RunState.Cancelled, run.State);
        Assert.Equal([StepState.Cancelled, StepState.Skipped], run.Steps.Select(s => s.State));
        Assert.Equal(1, _restic.Backups);
        Assert.Contains(_fx.Log.Tail(plan.Id, 50), l => l.Contains("cancelled"));
        Assert.Null(_fx.Runs.Active());
    }

    [Fact]
    public async Task Only_one_run_goes_at_a_time_and_the_next_can_start_after_it()
    {
        (TaskCompletionSource started, TaskCompletionSource go) = Hold();
        Run one = await _fx.PlanSync(_source, _first);
        Run two = await _fx.PlanSync(_source, _second);
        _fx.Service.Start(one.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), _fx.Ct);

        Assert.Equal("run_in_progress", Assert.Throws<DomainException>(() => _fx.Service.Start(two.Id, true)).Code);
        Assert.Equal(RunState.Planned, _fx.Runs.Find(two.Id)!.State);

        go.SetResult();
        await _fx.Service.WaitAsync(one.Id);
        Assert.Equal(RunState.Succeeded, _fx.Runs.Find(one.Id)!.State);

        Assert.Equal(RunState.Succeeded, (await _fx.RunToEnd(two)).State);
    }

    [Fact]
    public async Task Progress_is_visible_while_the_step_runs()
    {
        TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource go = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _restic.OnBackup = async (_, _, onProgress, ct) =>
        {
            onProgress!(new ResticBackupProgress(0.5, 1, 2, 50, 100));
            reported.SetResult();
            await go.Task.WaitAsync(ct);
            return Summary();
        };
        Run plan = await _fx.PlanSync(_source, _first);
        _fx.Service.Start(plan.Id, true);
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), _fx.Ct);

        RunStep step = _fx.Runs.Read(plan.Id, r => r.Steps[0]);
        Assert.Equal(0.5, step.Progress);
        Assert.Equal(50, step.BytesDone);

        go.SetResult();
        await _fx.Service.WaitAsync(plan.Id);
        Assert.Equal(1, _fx.Runs.Find(plan.Id)!.Steps[0].Progress);
    }

    [Fact]
    public async Task Shutdown_cancels_what_is_going()
    {
        (TaskCompletionSource started, _) = Hold();
        Run plan = await _fx.PlanSync(_source, _first);
        _fx.Service.Start(plan.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), _fx.Ct);

        await _fx.Service.ShutdownAsync();

        Assert.Equal(RunState.Cancelled, _fx.Runs.Find(plan.Id)!.State);
    }

    [Fact]
    public async Task An_unexpected_error_fails_the_step_and_keeps_the_stack_in_the_log_but_no_password()
    {
        _restic.OnBackup = (_, _, _, _) => throw new InvalidOperationException($"boom with {_first.Password} inside");
        Run plan = await _fx.PlanSync(_source, _first);

        Run run = await _fx.RunToEnd(plan);

        RunStep step = run.Steps[0];
        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal("unexpected", step.ErrorCode);
        Assert.DoesNotContain(_first.Password!, step.ErrorMessage);
        string log = string.Join('\n', _fx.Log.Tail(plan.Id, 100));
        Assert.Contains("InvalidOperationException", log);
        Assert.Contains("   at ", log.Replace("  at ", "   at "));
        Assert.DoesNotContain(_first.Password!, log);
    }

    [Fact]
    public async Task A_known_tool_error_is_logged_without_a_stack_and_without_the_password()
    {
        _restic.OnBackup = (_, _, _, _) => throw new ExternalToolException("restic_failed", $"Fatal: {_first.Password} rejected");
        Run plan = await _fx.PlanSync(_source, _first);

        Run run = await _fx.RunToEnd(plan);

        Assert.Equal("restic_failed", run.Steps[0].ErrorCode);
        Assert.DoesNotContain(_first.Password!, run.Steps[0].ErrorMessage);
        string log = string.Join('\n', _fx.Log.Tail(plan.Id, 100));
        Assert.DoesNotContain(_first.Password!, log);
        Assert.DoesNotContain("ExternalToolException", log);
    }

    [Fact]
    public async Task One_failed_target_does_not_stop_the_next_one()
    {
        int calls = 0;
        _restic.OnBackup = (_, _, _, _) => ++calls == 1
            ? throw new ExternalToolException("restic_failed", "first target failed")
            : Task.FromResult(Summary());
        Run plan = await _fx.PlanSync(_source);

        Run run = await _fx.RunToEnd(plan);

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal([StepState.Failed, StepState.Succeeded], run.Steps.Select(s => s.State));
    }

    [Fact]
    public async Task Unreadable_files_are_a_warning_and_the_run_still_succeeds()
    {
        _restic.OnBackup = (_, _, _, _) => Task.FromResult(Summary(true, "/s/secret: permission denied"));
        Run plan = await _fx.PlanSync(_source, _first);

        Run run = await _fx.RunToEnd(plan);

        Assert.Equal(RunState.Succeeded, run.State);
        Assert.Contains("backup_incomplete", run.Steps[0].Notes);
        Assert.Contains(_fx.Log.Tail(plan.Id, 50), l => l.Contains("Not saved: /s/secret"));
    }

    [Fact]
    public async Task The_history_is_saved_and_a_restart_in_the_middle_leaves_an_interrupted_run()
    {
        (TaskCompletionSource started, _) = Hold();
        Run plan = await _fx.PlanSync(_source, _first);
        _fx.Service.Start(plan.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), _fx.Ct);

        // What was saved at this moment is what a new process would find after a crash.
        RunCatalog afterCrash = new(new MemoryRunStore { }.WithRuns(_fx.Store.Saved), _fx.Time.Now.AddMinutes(1));
        Run found = afterCrash.Find(plan.Id)!;
        Assert.Equal(RunState.Interrupted, found.State);
        Assert.Equal(StepState.Interrupted, found.Steps[0].State);

        await _fx.Service.ShutdownAsync();
    }
}

internal static class MemoryRunStoreExtensions
{
    public static MemoryRunStore WithRuns(this MemoryRunStore store, List<Run> runs)
    {
        store.Save(runs.Select(Copy).ToList());
        return store;
    }

    private static Run Copy(Run run) => System.Text.Json.JsonSerializer.Deserialize(
        System.Text.Json.JsonSerializer.Serialize(run, CopyContext.Default.Run), CopyContext.Default.Run)!;
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Run))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(UseStringEnumConverter = true)]
internal sealed partial class CopyContext : System.Text.Json.Serialization.JsonSerializerContext;

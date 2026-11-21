using Backup.Infra.Process;
using Xunit;

namespace Backup.Infra.Tests;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task Output_and_exit_code_are_returned()
    {
        ProcessResult result = await _runner.RunAsync(new("printf", ["a\nb\n"]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal(["a", "b"], result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Failure_gives_exit_code_and_error_output()
    {
        ProcessResult result = await _runner.RunAsync(new("ls", ["/definitely/not/here"]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("/definitely/not/here", result.StandardError);
    }

    [Fact]
    public async Task Arguments_are_not_interpreted_by_a_shell()
    {
        const string tricky = "$(echo pwned); `id` | \"quoted\" 'x' *";
        ProcessResult result = await _runner.RunAsync(new("printf", ["%s", tricky]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(tricky, result.StandardOutput.TrimEnd('\n'));
    }

    [Fact]
    public async Task Environment_is_added_and_removed()
    {
        Environment.SetEnvironmentVariable("BACKUP_TEST_REMOVED", "x");
        Dictionary<string, string?> env = new() { ["BACKUP_TEST_SECRET"] = "s3cret", ["BACKUP_TEST_REMOVED"] = null };
        ProcessResult added = await _runner.RunAsync(new("printenv", ["BACKUP_TEST_SECRET"], env), cancellationToken: TestContext.Current.CancellationToken);
        ProcessResult removed = await _runner.RunAsync(new("printenv", ["BACKUP_TEST_REMOVED"], env), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("s3cret", added.StandardOutput.Trim());
        Assert.NotEqual(0, removed.ExitCode);
    }

    [Fact]
    public async Task Standard_input_is_delivered()
    {
        ProcessResult result = await _runner.RunAsync(new("cat", [], StandardInput: "hello\nworld\n"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("hello\nworld\n", result.StandardOutput);
    }

    [Fact]
    public async Task Lines_are_streamed_and_capture_can_be_turned_off()
    {
        List<string> lines = [];
        ProcessResult result = await _runner.RunAsync(new("printf", ["1\n2\n3\n"], CaptureStandardOutput: false), lines.Add, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(["1", "2", "3"], lines);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task Missing_program_is_an_external_tool_error()
    {
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _runner.RunAsync(new("/no/such/program", []), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("tool_not_started", error.Code);
    }

    [Fact]
    public async Task Cancel_kills_the_program()
    {
        using CancellationTokenSource cancel = new();
        Task<ProcessResult> run = _runner.RunAsync(new("sleep", ["60"]), cancellationToken: cancel.Token);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        DateTime start = DateTime.UtcNow;
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancel_before_start_never_runs_the_program()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _runner.RunAsync(new("sleep", ["60"]), cancellationToken: cancel.Token));
    }

    /// <summary>A program that was started in the background by a shell has SIGINT ignored, and so have its children.</summary>
    private static bool SigIntIsIgnored() =>
        File.ReadLines("/proc/self/status").FirstOrDefault(l => l.StartsWith("SigIgn:", StringComparison.Ordinal)) is string line
        && (Convert.ToUInt64(line["SigIgn:".Length..].Trim(), 16) & (1UL << 1)) != 0;

    [Fact]
    public async Task Cancel_first_asks_the_program_to_stop_so_that_it_can_clean_up()
    {
        Assert.SkipWhen(SigIntIsIgnored(), "SIGINT is ignored in this process, as when the tests are started in the background.");

        // A program that cleans up when it is interrupted, as restic and rclone do.
        List<string> lines = [];
        using CancellationTokenSource cancel = new();
        Task<ProcessResult> run = _runner.RunAsync(
            new("sh", ["-c", "trap 'echo cleaned-up; exit 130' INT; echo ready; sleep 60 & wait"]),
            line =>
            {
                lines.Add(line);
                if (line == "ready")
                {
                    cancel.Cancel();
                }
            },
            cancellationToken: cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Contains("cleaned-up", lines);
    }

    [Fact]
    public async Task A_program_that_ignores_every_request_is_killed_after_the_grace_period()
    {
        ProcessRunner patient = new(TimeSpan.FromMilliseconds(400));
        using CancellationTokenSource cancel = new();
        Task<ProcessResult> run = patient.RunAsync(
            new("sh", ["-c", "trap '' INT TERM; echo ready; sleep 60"]),
            line =>
            {
                if (line == "ready")
                {
                    cancel.Cancel();
                }
            },
            cancellationToken: cancel.Token);

        DateTime start = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        TimeSpan took = DateTime.UtcNow - start;
        Assert.InRange(took, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_program_that_ignores_SIGINT_is_asked_again_with_SIGTERM_before_it_is_killed()
    {
        // Only SIGINT is ignored, as in a program that a shell started in the background. SIGTERM, after a quarter of the grace period, ends it long before the kill.
        ProcessRunner patient = new(TimeSpan.FromSeconds(20));
        using CancellationTokenSource cancel = new();
        Task<ProcessResult> run = patient.RunAsync(
            new("sh", ["-c", "trap '' INT; echo ready; while true; do sleep 0.1; done"]),
            line =>
            {
                if (line == "ready")
                {
                    cancel.Cancel();
                }
            },
            cancellationToken: cancel.Token);

        DateTime start = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        TimeSpan took = DateTime.UtcNow - start;
        Assert.InRange(took, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
    }
}

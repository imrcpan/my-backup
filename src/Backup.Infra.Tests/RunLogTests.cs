using Backup.Infra.Logging;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class RunLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("runlog").FullName, "logs");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, true);

    [Fact]
    public void Lines_are_appended_with_a_time_and_read_back()
    {
        RunLog log = new(_dir, new FixedTime(new DateTimeOffset(2026, 11, 20, 1, 2, 3, TimeSpan.Zero)));
        log.Append("run1", "first", SecretRedactor.None);
        log.Append("run1", "second", SecretRedactor.None);
        Assert.Equal(["2026-11-20 01:02:03Z first", "2026-11-20 01:02:03Z second"], log.Tail("run1", 10));
        Assert.Equal(["2026-11-20 01:02:03Z second"], log.Tail("run1", 1));
        Assert.Empty(log.Tail("other", 10));
    }

    [Fact]
    public void Secrets_never_reach_the_file()
    {
        RunLog log = new(_dir);
        log.Append("run1", "opening with hunter2 and RESTIC_PASSWORD=abc", new SecretRedactor(["hunter2"]));
        string file = File.ReadAllText(Path.Combine(_dir, "run1.log"));
        Assert.DoesNotContain("hunter2", file);
        Assert.DoesNotContain("abc", file);
        Assert.Contains("***", file);
    }

    [Fact]
    public void A_line_stays_one_line()
    {
        RunLog log = new(_dir);
        log.Append("run1", "a\nb\r\nc", SecretRedactor.None);
        Assert.Single(log.Tail("run1", 10));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_run_id_cannot_leave_the_log_directory(string id)
    {
        RunLog log = new(_dir);
        Assert.Throws<ArgumentException>(() => log.Append(id, "x", SecretRedactor.None));
        Assert.Throws<ArgumentException>(() => log.Tail(id, 1));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

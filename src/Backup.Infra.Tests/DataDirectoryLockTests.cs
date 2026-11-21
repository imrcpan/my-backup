using Backup.Core;
using Backup.Infra.Persistence;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class DataDirectoryLockTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lock-test").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void The_directory_is_made_and_a_second_holder_is_refused_until_the_first_lets_go()
    {
        string dir = Path.Combine(_root, "data", "app");

        using (DataDirectoryLock first = DataDirectoryLock.Acquire(dir))
        {
            Assert.True(Directory.Exists(dir));
            DomainException refused = Assert.Throws<DomainException>(() => DataDirectoryLock.Acquire(dir));
            Assert.Equal("data_dir_in_use", refused.Code);
            Assert.Contains(dir, refused.Message);
            Assert.Contains($"(process {Environment.ProcessId})", refused.Message);
            Assert.NotNull(first);
        }

        using DataDirectoryLock again = DataDirectoryLock.Acquire(dir);
        Assert.NotNull(again);
    }

    [Fact]
    public void Different_directories_do_not_get_in_each_others_way()
    {
        using DataDirectoryLock a = DataDirectoryLock.Acquire(Path.Combine(_root, "a"));
        using DataDirectoryLock b = DataDirectoryLock.Acquire(Path.Combine(_root, "b"));
        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task A_holder_in_another_process_is_refused_and_the_lock_goes_with_the_process()
    {
        string dir = Path.Combine(_root, "shared");
        Directory.CreateDirectory(dir);
        // flock from a child process: the same lock that another instance of the application would hold.
        using System.Diagnostics.Process holder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("flock")
        {
            ArgumentList = { "--exclusive", Path.Combine(dir, ".lock"), "sleep", "60" },
            UseShellExecute = false,
        })!;
        bool refused = false;
        for (int i = 0; i < 100 && !refused; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            try
            {
                TryAcquire(dir);
            }
            catch (DomainException error) when (error.Code == "data_dir_in_use")
            {
                refused = true;
            }
        }

        Assert.True(refused, "the directory held by another process must be refused");

        holder.Kill(entireProcessTree: true);
        await holder.WaitForExitAsync(TestContext.Current.CancellationToken);

        using DataDirectoryLock free = DataDirectoryLock.Acquire(dir);
        Assert.NotNull(free);
    }

    [Fact]
    public void A_number_left_by_a_program_that_crashed_is_not_named_and_the_number_is_removed_at_the_end()
    {
        string dir = Path.Combine(_root, "crashed");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".lock.pid"), "999999999");

        using (DataDirectoryLock first = DataDirectoryLock.Acquire(dir))
        {
            Assert.Equal(Environment.ProcessId.ToString(), File.ReadAllText(Path.Combine(dir, ".lock.pid")));
            Assert.NotNull(first);
        }

        Assert.False(File.Exists(Path.Combine(dir, ".lock.pid")));

        // The lock file is held by a process that is not this one and left no number: the message does not guess one.
        File.WriteAllText(Path.Combine(dir, ".lock.pid"), "999999999");
        using System.Diagnostics.Process holder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("flock")
        {
            ArgumentList = { "--exclusive", Path.Combine(dir, ".lock"), "sleep", "30" },
            UseShellExecute = false,
        })!;
        DomainException refused = Assert.Throws<DomainException>(() => WaitUntilRefused(dir));
        Assert.DoesNotContain("999999999", refused.Message);
        Assert.DoesNotContain("(process", refused.Message);
        holder.Kill(entireProcessTree: true);
    }

    private static void WaitUntilRefused(string dir)
    {
        for (int i = 0; i < 100; i++)
        {
            Thread.Sleep(50);
            TryAcquire(dir);
        }
    }

    private static void TryAcquire(string dir)
    {
        using DataDirectoryLock held = DataDirectoryLock.Acquire(dir);
    }
}

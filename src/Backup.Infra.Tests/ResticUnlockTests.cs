
using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticUnlockTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private string LockDirectory => Path.Combine(_fx.Repository.Location, "locks");

    /// <summary>Kills restic in the middle of a backup, which leaves its lock in the repository.</summary>
    private async Task KillResticLeavingItsLock()
    {
        for (int i = 0; i < 4; i++)
        {
            _fx.Write($"big{i}.bin", new string('x', 20_000_000));
        }

        await _fx.Client.InitAsync(_fx.Repository, Ct);
        System.Diagnostics.ProcessStartInfo info = new("restic") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-r", _fx.Repository.Location, "backup", "--quiet", _fx.Source })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["RESTIC_PASSWORD"] = ResticFixture.Password;
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!;
        for (int i = 0; i < 100 && !(Directory.Exists(LockDirectory) && Directory.GetFiles(LockDirectory).Length > 0); i++)
        {
            await Task.Delay(50, Ct);
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(Ct);
    }

    [Fact]
    public async Task A_lock_left_by_a_killed_restic_blocks_a_check_until_it_is_removed()
    {
        await KillResticLeavingItsLock();
        Assert.NotEmpty(Directory.GetFiles(LockDirectory));

        ExternalToolException blocked = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.CheckAsync(_fx.Repository, ResticCheckMode.Structure, 10, null, Ct));
        Assert.Equal("repo_locked", blocked.Code);

        await _fx.Client.UnlockAsync(_fx.Repository, Ct);

        Assert.Empty(Directory.GetFiles(LockDirectory));
        Assert.True((await _fx.Client.CheckAsync(_fx.Repository, ResticCheckMode.Full, 10, null, Ct)).Ok);
    }

    [Fact]
    public async Task What_the_killed_backup_wrote_does_not_stop_the_next_backup_from_being_whole()
    {
        await KillResticLeavingItsLock();
        await _fx.Client.UnlockAsync(_fx.Repository, Ct);

        ResticBackupSummary summary = await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);

        Assert.False(summary.Incomplete);
        Assert.True((await _fx.Client.CheckAsync(_fx.Repository, ResticCheckMode.Full, 10, null, Ct)).Ok);
        string target = Path.Combine(_fx.Root, "restored");
        await _fx.Client.RestoreAsync(_fx.Repository, summary.SnapshotId, target, null, null, Ct);
        Assert.Equal(20_000_000, new FileInfo(Path.Combine(target, _fx.Source.TrimStart('/'), "big0.bin")).Length);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_fx.Source, "big3.bin")), File.ReadAllBytes(Path.Combine(target, _fx.Source.TrimStart('/'), "big3.bin")));
    }

    [Fact]
    public async Task Unlock_on_a_repository_without_locks_is_harmless_and_on_a_missing_one_an_error()
    {
        await _fx.Client.InitAsync(_fx.Repository, Ct);
        await _fx.Client.UnlockAsync(_fx.Repository, Ct);

        ResticRepository missing = _fx.Repository with { Location = Path.Combine(_fx.Root, "nothing") };
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.UnlockAsync(missing, Ct));
        Assert.Equal("repo_missing", error.Code);
    }
}

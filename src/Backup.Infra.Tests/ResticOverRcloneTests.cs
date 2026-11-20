using Backup.Infra.Process;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticOverRcloneTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("restic-rclone").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_repository_on_an_rclone_remote_works_end_to_end()
    {
        string config = Path.Combine(_root, "rclone.conf");
        File.WriteAllText(config, "[cloud]\ntype = local\n");
        string source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "over rclone");
        string repoDir = Path.Combine(_root, "remote-repo");
        ResticRepository repository = new($"rclone:cloud:{repoDir}", "pw-1");
        ResticClient client = new(new ProcessRunner(), new ResticOptions(RcloneConfigFile: config));

        Assert.False(await client.ExistsAsync(repository, Ct));
        await client.InitAsync(repository, Ct);
        ResticBackupSummary summary = await client.BackupAsync(repository, source, null, Ct);
        Assert.Equal(1, summary.FilesNew);
        Assert.True((await client.CheckAsync(repository, ResticCheckMode.Full, 10, null, Ct)).Ok);

        string target = Path.Combine(_root, "restored");
        await client.RestoreAsync(repository, summary.SnapshotId, target, null, null, Ct);
        Assert.Equal("over rclone", File.ReadAllText(Path.Combine(target, source.TrimStart('/'), "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(repoDir, "data")));
    }

    [Fact]
    public async Task Rclone_config_goes_to_restic_through_the_environment_only()
    {
        RecordingRunner runner = new();
        await new ResticClient(runner, new ResticOptions(RcloneConfigFile: "/x/rclone.conf")).ExistsAsync(new ResticRepository("rclone:a:/b", "p"), Ct);
        Assert.Equal("/x/rclone.conf", runner.Specs[0].Environment!["RCLONE_CONFIG"]);
        Assert.DoesNotContain("/x/rclone.conf", runner.Specs[0].Arguments);
    }
}

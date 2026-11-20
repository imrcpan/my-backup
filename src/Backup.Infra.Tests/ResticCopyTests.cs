using Backup.Infra.Process;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticCopyTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ResticRepository Other => new(Path.Combine(_fx.Root, "other-repo"), "another-password");

    private async Task<string> SourceWithOneSnapshot()
    {
        _fx.Write("a.txt", "copy me");
        await _fx.Client.InitAsync(_fx.Repository, Ct);
        return (await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct)).SnapshotId;
    }

    [Fact]
    public async Task A_snapshot_is_copied_into_a_repository_with_another_password_and_can_be_restored_there()
    {
        string snapshot = await SourceWithOneSnapshot();
        await _fx.Client.InitFromAsync(Other, _fx.Repository, Ct);

        List<string> lines = [];
        await _fx.Client.CopyAsync(Other, _fx.Repository, snapshot, lines.Add, Ct);

        ResticSnapshot original = (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Single();
        ResticSnapshot copy = Assert.Single(await _fx.Client.SnapshotsAsync(Other, Ct));
        Assert.True(original.SameContentAs(copy));
        Assert.NotEmpty(lines);

        string target = Path.Combine(_fx.Root, "restored");
        await _fx.Client.RestoreAsync(Other, copy.Id, target, null, null, Ct);
        Assert.Equal("copy me", File.ReadAllText(Path.Combine(target, _fx.Source.TrimStart('/'), "a.txt")));
    }

    [Fact]
    public async Task Copying_twice_leaves_one_snapshot()
    {
        string snapshot = await SourceWithOneSnapshot();
        await _fx.Client.InitFromAsync(Other, _fx.Repository, Ct);
        await _fx.Client.CopyAsync(Other, _fx.Repository, snapshot, null, Ct);
        await _fx.Client.CopyAsync(Other, _fx.Repository, snapshot, null, Ct);
        Assert.Single(await _fx.Client.SnapshotsAsync(Other, Ct));
    }

    [Fact]
    public async Task A_different_snapshot_is_not_the_same_content()
    {
        await SourceWithOneSnapshot();
        _fx.Write("a.txt", "changed");
        await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);
        ResticSnapshot[] snapshots = [.. await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)];
        Assert.False(snapshots[0].SameContentAs(snapshots[1]));
        Assert.True(snapshots[0].SameContentAs(snapshots[0]));
    }

    [Fact]
    public async Task Init_from_refuses_an_existing_repository_and_a_wrong_source_password()
    {
        await SourceWithOneSnapshot();
        ExternalToolException exists = await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.InitFromAsync(_fx.Repository, _fx.Repository, Ct));
        Assert.Equal("repo_exists", exists.Code);

        ExternalToolException wrong = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.InitFromAsync(Other, _fx.Repository with { Password = "bad-source-pw" }, Ct));
        Assert.Equal("repo_wrong_password", wrong.Code);
        Assert.DoesNotContain("bad-source-pw", wrong.Message);
    }

    [Fact]
    public async Task Both_passwords_stay_out_of_the_arguments_and_the_error_text()
    {
        RecordingRunner runner = new();
        runner.Answer(1, "", $"Fatal: {Other.Password} and {_fx.Repository.Password} failed");
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => new ResticClient(runner).CopyAsync(Other, _fx.Repository, "abcdef12", null, Ct));

        ProcessSpec spec = runner.Specs[0];
        Assert.DoesNotContain(Other.Password, string.Join(' ', spec.Arguments));
        Assert.DoesNotContain(_fx.Repository.Password, string.Join(' ', spec.Arguments));
        Assert.Equal(_fx.Repository.Password, spec.Environment!["RESTIC_FROM_PASSWORD"]);
        Assert.Equal(Other.Password, spec.Environment["RESTIC_PASSWORD"]);
        Assert.DoesNotContain(Other.Password, error.Message);
        Assert.DoesNotContain(_fx.Repository.Password, error.Message);
    }

    [Fact]
    public async Task Snapshot_id_that_could_be_an_option_is_refused()
    {
        RecordingRunner runner = new();
        await Assert.ThrowsAsync<ExternalToolException>(() => new ResticClient(runner).CopyAsync(Other, _fx.Repository, "--all", null, Ct));
        Assert.Empty(runner.Specs);
    }
}

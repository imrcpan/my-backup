using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticPasswordTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("pass word  with spaces ")]
    [InlineData("ünï©ödé-密码-🔑")]
    [InlineData("quotes \"'` $HOME $(id) ; | & <> \\")]
    [InlineData("a")]
    [InlineData("--password-file=/etc/passwd")]
    public async Task A_password_of_any_shape_opens_the_repository_it_made_and_not_another(string password)
    {
        ResticRepository repository = _fx.Repository with { Password = password };
        _fx.Write("a.txt", "secret contents");

        await _fx.Client.InitAsync(repository, Ct);
        ResticBackupSummary summary = await _fx.Client.BackupAsync(repository, _fx.Source, null, Ct);

        Assert.Single(await _fx.Client.SnapshotsAsync(repository, Ct));
        string target = Path.Combine(_fx.Root, "restored");
        await _fx.Client.RestoreAsync(repository, summary.SnapshotId, target, null, null, Ct);
        Assert.Equal("secret contents", File.ReadAllText(Path.Combine(target, _fx.Source.TrimStart('/'), "a.txt")));

        ExternalToolException wrong = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.SnapshotsAsync(repository with { Password = password + "x" }, Ct));
        Assert.Equal("repo_wrong_password", wrong.Code);
    }

    [Fact]
    public async Task A_password_with_a_line_break_works_too_and_is_taken_whole()
    {
        ResticRepository repository = _fx.Repository with { Password = "first line\nsecond line" };
        await _fx.Client.InitAsync(repository, Ct);
        Assert.Empty(await _fx.Client.SnapshotsAsync(repository, Ct));
        ExternalToolException wrong = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.SnapshotsAsync(repository with { Password = "first line" }, Ct));
        Assert.Equal("repo_wrong_password", wrong.Code);
    }
}

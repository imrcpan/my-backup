using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticRepositoryTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Missing_repository_does_not_exist_and_init_creates_it()
    {
        Assert.False(await _fx.Client.ExistsAsync(_fx.Repository, TestContext.Current.CancellationToken));
        await _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken);
        Assert.True(await _fx.Client.ExistsAsync(_fx.Repository, TestContext.Current.CancellationToken));
        Assert.Empty(await _fx.Client.SnapshotsAsync(_fx.Repository, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Init_refuses_to_touch_an_existing_repository()
    {
        await _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken);
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken));
        Assert.Equal("repo_exists", error.Code);
    }

    [Fact]
    public async Task Wrong_password_is_reported_without_the_password()
    {
        await _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken);
        ResticRepository wrong = _fx.Repository with { Password = "not-the-password" };
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.SnapshotsAsync(wrong, TestContext.Current.CancellationToken));
        Assert.Equal("repo_wrong_password", error.Code);
        Assert.DoesNotContain("not-the-password", error.Message);
        await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.ExistsAsync(wrong, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Snapshots_of_a_missing_repository_say_so()
    {
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => _fx.Client.SnapshotsAsync(_fx.Repository, TestContext.Current.CancellationToken));
        Assert.Equal("repo_missing", error.Code);
    }

    [Fact]
    public async Task Missing_restic_program_is_an_error()
    {
        ResticClient client = new(new Backup.Infra.Process.ProcessRunner(), new ResticOptions("/no/such/restic"));
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => client.ExistsAsync(_fx.Repository, TestContext.Current.CancellationToken));
        Assert.Equal("tool_not_started", error.Code);
    }

    [Fact]
    public async Task Password_is_never_on_the_command_line()
    {
        RecordingRunner runner = new();
        ResticClient client = new(runner);
        await client.ExistsAsync(_fx.Repository, TestContext.Current.CancellationToken);
        Backup.Infra.Process.ProcessSpec spec = Assert.Single(runner.Specs);
        Assert.DoesNotContain(ResticFixture.Password, string.Join(' ', spec.Arguments));
        Assert.Equal(ResticFixture.Password, spec.Environment!["RESTIC_PASSWORD"]);
        Assert.Null(spec.Environment["RESTIC_REPOSITORY"]);
        Assert.Null(spec.Environment["RESTIC_PASSWORD_COMMAND"]);
        Assert.Equal(["-r", _fx.Repository.Location], spec.Arguments.Take(2));
    }
}

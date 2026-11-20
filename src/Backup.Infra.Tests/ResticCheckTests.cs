using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticCheckTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Prepare()
    {
        for (int i = 0; i < 20; i++)
        {
            _fx.Write($"f{i}.txt", new string((char)('a' + i), 5000 + i));
        }

        await _fx.Client.InitAsync(_fx.Repository, Ct);
        await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct);
    }

    private void CorruptData()
    {
        string file = Directory.EnumerateFiles(Path.Combine(_fx.Repository.Location, "data"), "*", SearchOption.AllDirectories).First();
        byte[] bytes = File.ReadAllBytes(file);
        for (int i = 10; i < bytes.Length - 10; i += 7)
        {
            bytes[i] ^= 0xFF;
        }

        File.WriteAllBytes(file, bytes);
    }

    [Theory]
    [InlineData(ResticCheckMode.Structure)]
    [InlineData(ResticCheckMode.Subset)]
    [InlineData(ResticCheckMode.Full)]
    public async Task Healthy_repository_passes(ResticCheckMode mode)
    {
        await Prepare();
        List<string> lines = [];
        ResticCheckResult result = await _fx.Client.CheckAsync(_fx.Repository, mode, 50, lines.Add, Ct);
        Assert.True(result.Ok);
        Assert.Contains("no errors were found", result.Output);
        Assert.NotEmpty(lines);
    }

    [Fact]
    public async Task Damaged_data_is_found_by_reading_the_data_but_is_a_result_not_an_exception()
    {
        await Prepare();
        CorruptData();
        ResticCheckResult full = await _fx.Client.CheckAsync(_fx.Repository, ResticCheckMode.Full, 10, null, Ct);
        Assert.False(full.Ok);
        Assert.NotEmpty(full.Output);
    }

    [Fact]
    public async Task Wrong_password_and_missing_repository_are_exceptions()
    {
        ExternalToolException missing = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.CheckAsync(_fx.Repository, ResticCheckMode.Structure, 10, null, Ct));
        Assert.Equal("repo_missing", missing.Code);

        await Prepare();
        ExternalToolException wrong = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.CheckAsync(_fx.Repository with { Password = "nope" }, ResticCheckMode.Structure, 10, null, Ct));
        Assert.Equal("repo_wrong_password", wrong.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Subset_share_must_be_a_percentage(int percent)
    {
        RecordingRunner runner = new();
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => new ResticClient(runner).CheckAsync(_fx.Repository, ResticCheckMode.Subset, percent, null, Ct));
        Assert.Equal("check_subset_invalid", error.Code);
        Assert.Empty(runner.Specs);
    }

    [Fact]
    public async Task Modes_become_restic_flags()
    {
        RecordingRunner runner = new();
        ResticClient client = new(runner);
        await client.CheckAsync(_fx.Repository, ResticCheckMode.Structure, 10, null, Ct);
        await client.CheckAsync(_fx.Repository, ResticCheckMode.Subset, 25, null, Ct);
        await client.CheckAsync(_fx.Repository, ResticCheckMode.Full, 10, null, Ct);
        Assert.Equal(["check"], runner.Specs[0].Arguments.Skip(2));
        Assert.Equal(["check", "--read-data-subset", "25%"], runner.Specs[1].Arguments.Skip(2));
        Assert.Equal(["check", "--read-data"], runner.Specs[2].Arguments.Skip(2));
    }
}

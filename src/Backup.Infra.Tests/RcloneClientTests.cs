using Backup.Infra.Process;
using Backup.Infra.Rclone;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class RcloneClientTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rclone-test").FullName;
    private readonly RcloneClient _client;
    private readonly string _config;

    public RcloneClientTests()
    {
        _config = Path.Combine(_root, "rclone.conf");
        File.WriteAllText(_config, "[disk]\ntype = local\n\n[other]\ntype = local\n");
        _client = new RcloneClient(new ProcessRunner(), new RcloneOptions(ConfigFile: _config));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Remotes_are_listed_without_the_colon()
    {
        Assert.Equal(["disk", "other"], (await _client.ListRemotesAsync(Ct)).Order());
    }

    [Fact]
    public async Task Configured_remote_is_ready_and_unknown_one_is_not_configured()
    {
        Assert.Equal(RemoteState.Ready, (await _client.CheckRemoteAsync("disk", Ct)).State);
        Assert.Equal(RemoteState.NotConfigured, (await _client.CheckRemoteAsync("gdrive", Ct)).State);
    }

    [Fact]
    public async Task Directories_are_listed_and_files_are_not()
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        Directory.CreateDirectory(Path.Combine(dir, "Beta"));
        Directory.CreateDirectory(Path.Combine(dir, "alpha"));
        File.WriteAllText(Path.Combine(dir, "file.txt"), "x");

        IReadOnlyList<RcloneEntry> entries = await _client.ListDirectoriesAsync("disk", dir + "/", Ct);

        Assert.Equal(["alpha", "Beta"], entries.Select(e => e.Name));
        Assert.All(entries, e => Assert.True(e.IsDir));
    }

    [Fact]
    public async Task Listing_a_missing_directory_is_an_error()
    {
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _client.ListDirectoriesAsync("disk", Path.Combine(_root, "missing"), Ct));
        Assert.Equal("rclone_failed", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("-x")]
    [InlineData("a b")]
    [InlineData("a/b")]
    public async Task Bad_remote_names_never_reach_rclone(string name)
    {
        RecordingRunner runner = new();
        RcloneClient client = new(runner);
        await Assert.ThrowsAsync<ExternalToolException>(() => client.CheckRemoteAsync(name, Ct));
        await Assert.ThrowsAsync<ExternalToolException>(() => client.ListDirectoriesAsync(name, "/", Ct));
        await Assert.ThrowsAsync<ExternalToolException>(() => client.CreateGoogleDriveRemoteAsync(name, null, Ct));
        Assert.Empty(runner.Specs);
    }

    [Fact]
    public async Task Unreachable_remote_gives_the_message_of_rclone()
    {
        RecordingRunner runner = new();
        runner.Answer(0, "gdrive:\n");
        runner.Answer(1, "", "Failed to create file system: couldn't connect");
        RemoteStatus status = await new RcloneClient(runner).CheckRemoteAsync("gdrive", Ct);
        Assert.Equal(RemoteState.Unreachable, status.State);
        Assert.Contains("couldn't connect", status.Message);
    }

    [Fact]
    public async Task Creating_a_drive_remote_passes_the_arguments_one_by_one()
    {
        RecordingRunner runner = new();
        await new RcloneClient(runner, new RcloneOptions(ConfigFile: "/c.conf")).CreateGoogleDriveRemoteAsync("gdrive", null, Ct);
        Assert.Equal(["--config", "/c.conf", "config", "create", "gdrive", "drive", "scope=drive"], runner.Specs[0].Arguments);
    }

    [Fact]
    public async Task Tokens_printed_by_rclone_are_masked_in_the_lines_that_are_passed_on()
    {
        RecordingRunner runner = new();
        runner.Answer(0, """{"access_token":"ya29.secret","expiry":"x"}""");
        List<string> lines = [];
        await new RcloneClient(runner).CreateGoogleDriveRemoteAsync("gdrive", lines.Add, Ct);
        Assert.DoesNotContain("ya29", string.Join('\n', lines));
    }

    [Fact]
    public async Task Failure_message_has_no_token()
    {
        RecordingRunner runner = new();
        runner.Answer(1, "", """Failed: {"access_token":"ya29.secret"}""");
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => new RcloneClient(runner).ListRemotesAsync(Ct));
        Assert.DoesNotContain("ya29", error.Message);
    }
}

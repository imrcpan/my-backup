using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticRestoreTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Restored(string target, string relative) => Path.Combine(target, _fx.Source.TrimStart('/'), relative);

    private async Task<string> BackedUp()
    {
        _fx.Write("a.txt", "hello");
        _fx.Write("sub/b.txt", "world!");
        _fx.Write("sub/odd[1]*?.txt", "special");
        await _fx.Client.InitAsync(_fx.Repository, Ct);
        return (await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct)).SnapshotId;
    }

    [Fact]
    public async Task Whole_snapshot_goes_to_a_new_directory()
    {
        string snapshot = await BackedUp();
        string target = Path.Combine(_fx.Root, "restored");

        ResticRestoreSummary summary = await _fx.Client.RestoreAsync(_fx.Repository, snapshot, target, null, null, Ct);

        Assert.True(summary.FilesRestored >= 3);
        Assert.Equal("hello", File.ReadAllText(Restored(target, "a.txt")));
        Assert.Equal("world!", File.ReadAllText(Restored(target, "sub/b.txt")));
    }

    [Fact]
    public async Task Existing_empty_directory_is_fine_and_original_files_stay_untouched()
    {
        string snapshot = await BackedUp();
        string target = Directory.CreateDirectory(Path.Combine(_fx.Root, "empty")).FullName;
        File.WriteAllText(Path.Combine(_fx.Source, "a.txt"), "changed after backup");

        await _fx.Client.RestoreAsync(_fx.Repository, snapshot, target, null, null, Ct);

        Assert.Equal("hello", File.ReadAllText(Restored(target, "a.txt")));
        Assert.Equal("changed after backup", File.ReadAllText(Path.Combine(_fx.Source, "a.txt")));
    }

    [Fact]
    public async Task Target_with_anything_in_it_is_refused_and_left_alone()
    {
        string snapshot = await BackedUp();
        string target = Directory.CreateDirectory(Path.Combine(_fx.Root, "busy")).FullName;
        string keep = Path.Combine(target, "keep.txt");
        File.WriteAllText(keep, "mine");

        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.RestoreAsync(_fx.Repository, snapshot, target, null, null, Ct));

        Assert.Equal("restore_target_not_empty", error.Code);
        Assert.Equal("mine", File.ReadAllText(keep));
        Assert.Equal(["keep.txt"], Directory.GetFileSystemEntries(target).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_file_as_target_is_refused()
    {
        string snapshot = await BackedUp();
        string file = Path.Combine(_fx.Root, "file");
        File.WriteAllText(file, "x");
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.RestoreAsync(_fx.Repository, snapshot, file, null, null, Ct));
        Assert.Equal("restore_target_not_empty", error.Code);
    }

    [Fact]
    public async Task Only_the_chosen_paths_are_restored_even_when_they_look_like_patterns()
    {
        string snapshot = await BackedUp();
        string target = Path.Combine(_fx.Root, "partial");

        ResticRestoreSummary summary = await _fx.Client.RestoreAsync(
            _fx.Repository, snapshot, target, [Path.Combine(_fx.Source, "sub/odd[1]*?.txt")], null, Ct);

        Assert.Equal("special", File.ReadAllText(Restored(target, "sub/odd[1]*?.txt")));
        Assert.False(File.Exists(Restored(target, "a.txt")));
        Assert.False(File.Exists(Restored(target, "sub/b.txt")));
        Assert.True(summary.FilesRestored >= 1);
    }

    [Fact]
    public async Task A_directory_path_restores_its_content()
    {
        string snapshot = await BackedUp();
        string target = Path.Combine(_fx.Root, "dir");
        await _fx.Client.RestoreAsync(_fx.Repository, snapshot, target, [Path.Combine(_fx.Source, "sub")], null, Ct);
        Assert.True(File.Exists(Restored(target, "sub/b.txt")));
        Assert.False(File.Exists(Restored(target, "a.txt")));
    }

    [Fact]
    public async Task Unknown_snapshot_fails_and_leaves_no_file_behind()
    {
        await BackedUp();
        string target = Path.Combine(_fx.Root, "none");
        await Assert.ThrowsAsync<ExternalToolException>(
            () => _fx.Client.RestoreAsync(_fx.Repository, "ffffffff", target, null, null, Ct));
        Assert.Empty(Directory.Exists(target) ? Directory.GetFileSystemEntries(target) : []);
    }

    [Theory]
    [InlineData("/a/b", "/a/b")]
    [InlineData("/a/[x]*?", "/a/\\[x]\\*\\?")]
    [InlineData("/a\\b", "/a\\\\b")]
    public void Patterns_are_escaped(string path, string expected)
    {
        Assert.Equal(expected, ResticClient.EscapePattern(path));
    }
}

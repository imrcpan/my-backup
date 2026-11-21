using Backup.Infra.Restic;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticTagTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_snapshot_is_tagged_and_is_then_known_as_incomplete_and_the_others_are_not()
    {
        _fx.Write("a.txt", "x");
        await _fx.Client.InitAsync(_fx.Repository, Ct);
        string first = (await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct)).SnapshotId;
        _fx.Write("b.txt", "y");
        string second = (await _fx.Client.BackupAsync(_fx.Repository, _fx.Source, null, Ct)).SnapshotId;

        ResticSnapshot before = (await _fx.Client.SnapshotsAsync(_fx.Repository, Ct)).Single(s => s.Id == second);

        await _fx.Client.TagAsync(_fx.Repository, second, ResticSnapshot.IncompleteTag, Ct);

        // restic does not change a snapshot: it makes a new one with the tag and removes the old one, so the id is another.
        IReadOnlyList<ResticSnapshot> snapshots = await _fx.Client.SnapshotsAsync(_fx.Repository, Ct);
        Assert.Equal(2, snapshots.Count);
        Assert.False(snapshots.Single(s => s.Id == first).IsIncomplete);
        ResticSnapshot tagged = snapshots.Single(s => s.IsIncomplete);
        Assert.Equal(["incomplete"], tagged.Tags);
        Assert.NotEqual(before.Id, tagged.Id);
        Assert.True(before.SameContentAs(tagged), "the tree, the paths, the time and the host stay");
        Assert.Equal(before.Summary!.FilesNew, tagged.Summary!.FilesNew);
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task A_snapshot_id_that_is_not_one_never_reaches_restic(string id)
    {
        RecordingRunner runner = new();
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => new ResticClient(runner).TagAsync(_fx.Repository, id, "incomplete", Ct));
        Assert.Equal("snapshot_id_invalid", error.Code);
        Assert.Empty(runner.Specs);
    }

    [Theory]
    [InlineData("--remove")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("a,b")]
    public async Task A_tag_that_could_be_an_option_or_a_list_never_reaches_restic(string tag)
    {
        RecordingRunner runner = new();
        ExternalToolException error = await Assert.ThrowsAsync<ExternalToolException>(() => new ResticClient(runner).TagAsync(_fx.Repository, "abcdef12", tag, Ct));
        Assert.Equal("tag_invalid", error.Code);
        Assert.Empty(runner.Specs);
    }
}

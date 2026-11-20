using Xunit;

namespace Backup.Infra.Tests;

public sealed class ResticSnapshotTests : IDisposable
{
    private readonly ResticFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Snapshots_and_listing_read_what_restic_wrote()
    {
        _fx.Write("a.txt", "hello");
        _fx.Write("sub/b.txt", "world!");
        await _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken);
        Assert.True((await Run("backup", _fx.Source)).Succeeded);

        var snapshots = await _fx.Client.SnapshotsAsync(_fx.Repository, TestContext.Current.CancellationToken);
        var snapshot = Assert.Single(snapshots);
        Assert.Equal([_fx.Source], snapshot.Paths);
        Assert.Equal(2, snapshot.Summary!.FilesNew);
        Assert.Equal(8, snapshot.ShortId.Length);

        var top = await _fx.Client.ListAsync(_fx.Repository, snapshot.Id, "/", TestContext.Current.CancellationToken);
        Assert.Equal(["tmp"], top.Select(n => n.Name));
        var children = await _fx.Client.ListAsync(_fx.Repository, snapshot.ShortId, _fx.Source, TestContext.Current.CancellationToken);
        Assert.Equal(["a.txt", "sub"], children.Select(n => n.Name).Order());
        Assert.Equal("dir", children.Single(n => n.Name == "sub").Type);
        Assert.Equal(5, children.Single(n => n.Name == "a.txt").Size);
    }

    [Fact]
    public async Task Snapshots_are_ordered_oldest_first()
    {
        _fx.Write("a.txt", "1");
        await _fx.Client.InitAsync(_fx.Repository, TestContext.Current.CancellationToken);
        await Run("backup", _fx.Source);
        _fx.Write("a.txt", "2222");
        await Run("backup", _fx.Source);
        var snapshots = await _fx.Client.SnapshotsAsync(_fx.Repository, TestContext.Current.CancellationToken);
        Assert.Equal(2, snapshots.Count);
        Assert.True(snapshots[0].Time < snapshots[1].Time);
    }

    private Task<Backup.Infra.Process.ProcessResult> Run(params string[] args) =>
        new Backup.Infra.Process.ProcessRunner().RunAsync(new("restic", ["-r", _fx.Repository.Location, .. args],
            new Dictionary<string, string?> { ["RESTIC_PASSWORD"] = ResticFixture.Password }));
}

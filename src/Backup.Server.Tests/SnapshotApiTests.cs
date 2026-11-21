using System.Net;
using System.Text.Json;
using Backup.Infra.Process;
using Backup.Infra.Restic;
using Xunit;

namespace Backup.Server.Tests;

public sealed class SnapshotApiTests : IDisposable
{
    private readonly ApiFactory _api = new();
    private string _group = "";
    private string _repoId = "";
    private string _sourcePath = "";

    public void Dispose() => _api.Dispose();

    /// <summary>An encrypted object on the cloud drive, with a repository made behind the back of the server.</summary>
    private async Task Setup(int snapshots)
    {
        _group = (await _api.Send("POST", "/api/groups", """{"name":"Media"}""")).Body.GetProperty("id").GetString()!;
        string repoPath = Path.Combine(_api.Cloud, "repo");
        _repoId = (await _api.Send("POST", $"/api/groups/{_group}/objects", $$"""{"deviceId":"gdrive","path":"{{repoPath}}","encrypted":true,"password":"repo-pw"}""")).Body.GetProperty("id").GetString()!;
        _sourcePath = Directory.CreateDirectory(Path.Combine(_api.Root, "docs/sub")).Parent!.FullName;
        File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_sourcePath, "sub/b.txt"), "world!");

        ResticClient restic = new(new ProcessRunner(), new ResticOptions(RcloneConfigFile: _api.RcloneConfig));
        ResticRepository repository = new($"rclone:gdrive:{repoPath}", "repo-pw");
        await restic.InitAsync(repository, TestContext.Current.CancellationToken);
        for (int i = 0; i < snapshots; i++)
        {
            File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), $"version {i}");
            await restic.BackupAsync(repository, _sourcePath, null, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Snapshots_are_listed_newest_first()
    {
        await Setup(2);
        JsonElement list = (await _api.Send("GET", $"/api/groups/{_group}/objects/{_repoId}/snapshots")).Body;
        Assert.Equal(2, list.GetArrayLength());
        Assert.True(list[0].GetProperty("time").GetDateTimeOffset() > list[1].GetProperty("time").GetDateTimeOffset());
        Assert.Equal(8, list[0].GetProperty("shortId").GetString()!.Length);
        Assert.Equal(2, list[0].GetProperty("files").GetInt64());
        Assert.Equal([_sourcePath], list[0].GetProperty("paths").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task A_snapshot_from_an_incomplete_backup_is_marked_as_such_in_the_list()
    {
        await Setup(2);
        ResticClient restic = new(new ProcessRunner(), new ResticOptions(RcloneConfigFile: _api.RcloneConfig));
        ResticRepository repository = new($"rclone:gdrive:{Path.Combine(_api.Cloud, "repo")}", "repo-pw");
        string newest = (await restic.SnapshotsAsync(repository, TestContext.Current.CancellationToken))[^1].Id;
        await restic.TagAsync(repository, newest, ResticSnapshot.IncompleteTag, TestContext.Current.CancellationToken);

        JsonElement list = (await _api.Send("GET", $"/api/groups/{_group}/objects/{_repoId}/snapshots")).Body;

        Assert.Equal(2, list.GetArrayLength());
        Assert.Single(list.EnumerateArray(), s => s.GetProperty("incomplete").GetBoolean());
        Assert.Single(list.EnumerateArray(), s => !s.GetProperty("incomplete").GetBoolean());
    }

    [Fact]
    public async Task The_files_of_a_snapshot_are_browsed_directory_by_directory_with_directories_first()
    {
        await Setup(1);
        string snapshot = (await _api.Send("GET", $"/api/groups/{_group}/objects/{_repoId}/snapshots")).Body[0].GetProperty("id").GetString()!;
        string files = $"/api/groups/{_group}/objects/{_repoId}/snapshots/{snapshot}/files";

        JsonElement top = (await _api.Send("GET", files)).Body;
        Assert.Equal("tmp", top[0].GetProperty("name").GetString());

        JsonElement children = (await _api.Send("GET", $"{files}?path={Uri.EscapeDataString(_sourcePath)}")).Body;
        Assert.Equal(["sub", "a.txt"], children.EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        Assert.Equal("dir", children[0].GetProperty("type").GetString());
        Assert.Equal(9, children[1].GetProperty("size").GetInt64());

        JsonElement sub = (await _api.Send("GET", $"{files}?path={Uri.EscapeDataString(_sourcePath + "/sub/")}")).Body;
        Assert.Equal(["b.txt"], sub.EnumerateArray().Select(n => n.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Short_or_unknown_snapshot_ids_and_bad_paths_are_refused()
    {
        await Setup(1);
        string files = $"/api/groups/{_group}/objects/{_repoId}/snapshots";
        Assert.Equal("snapshot_not_found", (await _api.Send("GET", $"{files}/abc/files")).Body.GetProperty("code").GetString());
        Assert.Equal("snapshot_not_found", (await _api.Send("GET", $"{files}/deadbeef/files")).Body.GetProperty("code").GetString());
        string snapshot = (await _api.Send("GET", files)).Body[0].GetProperty("id").GetString()!;
        Assert.Equal("path_invalid", (await _api.Send("GET", $"{files}/{snapshot}/files?path=relative")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_plain_object_a_missing_repository_a_wrong_password_and_unknown_ids_are_refused()
    {
        await Setup(1);
        string plain = (await _api.Send("POST", $"/api/groups/{_group}/objects", """{"deviceId":"local","path":"/plain","encrypted":false}""")).Body.GetProperty("id").GetString()!;
        Assert.Equal("object_not_a_repository", (await _api.Send("GET", $"/api/groups/{_group}/objects/{plain}/snapshots")).Body.GetProperty("code").GetString());

        string nothing = (await _api.Send("POST", $"/api/groups/{_group}/objects", $$"""{"deviceId":"gdrive","path":"{{Path.Combine(_api.Cloud, "none")}}","encrypted":true,"password":"x"}""")).Body.GetProperty("id").GetString()!;
        Assert.Equal("repo_missing", (await _api.Send("GET", $"/api/groups/{_group}/objects/{nothing}/snapshots")).Body.GetProperty("code").GetString());

        await _api.Send("PUT", $"/api/groups/{_group}/objects/{_repoId}", $$"""{"deviceId":"gdrive","path":"{{Path.Combine(_api.Cloud, "repo")}}","encrypted":true,"password":"wrong-password-here"}""");
        (HttpStatusCode status, JsonElement body) = await _api.Send("GET", $"/api/groups/{_group}/objects/{_repoId}/snapshots");
        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal("repo_wrong_password", body.GetProperty("code").GetString());
        Assert.DoesNotContain("wrong-password-here", body.GetRawText());

        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", $"/api/groups/{_group}/objects/nope/snapshots")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", "/api/groups/nope/objects/x/snapshots")).Status);
    }
}

using System.Net;
using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class ObjectFilesApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_files_of_a_plain_object_are_browsed_and_an_encrypted_one_points_to_its_snapshots()
    {
        string dir = Directory.CreateDirectory(Path.Combine(_api.Root, "docs/sub")).Parent!.FullName;
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(dir, "sub/b.txt"), "x");
        string group = (await _api.Send("POST", "/api/groups", """{"name":"G"}""")).Body.GetProperty("id").GetString()!;
        string plain = (await _api.Send("POST", $"/api/groups/{group}/objects", $$"""{"deviceId":"local","path":"{{dir}}","encrypted":false}""")).Body.GetProperty("id").GetString()!;
        string repo = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/r","encrypted":true,"password":"pw"}""")).Body.GetProperty("id").GetString()!;

        JsonElement top = (await _api.Send("GET", $"/api/groups/{group}/objects/{plain}/files")).Body;
        Assert.Equal(["sub", "a.txt"], top.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        Assert.Equal(5, top.GetProperty("entries")[1].GetProperty("size").GetInt64());
        Assert.False(top.GetProperty("truncated").GetBoolean());

        JsonElement sub = (await _api.Send("GET", $"/api/groups/{group}/objects/{plain}/files?path=/sub")).Body;
        Assert.Equal("/sub/b.txt", sub.GetProperty("entries")[0].GetProperty("path").GetString());

        (HttpStatusCode status, JsonElement body) = await _api.Send("GET", $"/api/groups/{group}/objects/{repo}/files");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("object_is_a_repository", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", $"/api/groups/{group}/objects/{plain}/files?path=/nope")).Status);
    }
}

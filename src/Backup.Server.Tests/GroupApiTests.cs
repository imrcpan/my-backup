using System.Net;
using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class GroupApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<string> NewGroup(string name = "Media")
    {
        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", "/api/groups", $$"""{"name":"{{name}}"}""");
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task A_group_is_made_from_a_name_alone_and_starts_empty()
    {
        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", "/api/groups", """{"name":"  Photos "}""");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Photos", body.GetProperty("name").GetString());
        Assert.Equal(0, body.GetProperty("objects").GetArrayLength());

        JsonElement list = (await _api.Send("GET", "/api/groups")).Body;
        Assert.Equal(["Photos"], list.EnumerateArray().Select(g => g.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Group_names_are_checked()
    {
        await NewGroup("Photos");
        (HttpStatusCode taken, JsonElement takenBody) = await _api.Send("POST", "/api/groups", """{"name":"photos"}""");
        Assert.Equal(HttpStatusCode.Conflict, taken);
        Assert.Equal("group_name_taken", takenBody.GetProperty("code").GetString());
        Assert.Equal("group_name_invalid", (await _api.Send("POST", "/api/groups", """{"name":" "}""")).Body.GetProperty("code").GetString());
        Assert.Equal("group_name_invalid", (await _api.Send("POST", "/api/groups", """{}""")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_group_is_renamed_and_removed()
    {
        string id = await NewGroup("Old");
        Assert.Equal("New", (await _api.Send("PUT", $"/api/groups/{id}", """{"name":"New"}""")).Body.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _api.Send("DELETE", $"/api/groups/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("DELETE", $"/api/groups/{id}")).Status);
        Assert.Equal(0, (await _api.Send("GET", "/api/groups")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Objects_have_their_own_encryption_and_a_slash_at_the_end_means_nothing()
    {
        string group = await NewGroup();
        JsonElement a = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/data/a/","encrypted":true,"password":"s3cret"}""")).Body;
        JsonElement b = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/data/b","encrypted":false,"password":"ignored"}""")).Body;

        Assert.Equal("/data/a", a.GetProperty("path").GetString());
        Assert.True(a.GetProperty("encrypted").GetBoolean());
        Assert.True(a.GetProperty("hasPassword").GetBoolean());
        Assert.False(b.GetProperty("encrypted").GetBoolean());
        Assert.False(b.GetProperty("hasPassword").GetBoolean());

        (HttpStatusCode again, JsonElement body) = await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/data/b/","encrypted":false}""");
        Assert.Equal(HttpStatusCode.Conflict, again);
        Assert.Equal("object_exists", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task No_response_ever_holds_a_password_but_the_file_does_so_it_is_not_asked_for_again()
    {
        string group = await NewGroup();
        (_, JsonElement created) = await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/data/a","encrypted":true,"password":"hunter2-secret"}""");
        string objectId = created.GetProperty("id").GetString()!;
        string[] answers =
        [
            created.GetRawText(),
            (await _api.Send("GET", "/api/groups")).Body.GetRawText(),
            (await _api.Send("PUT", $"/api/groups/{group}/objects/{objectId}", """{"deviceId":"local","path":"/data/a","encrypted":true,"password":""}""")).Body.GetRawText(),
            (await _api.Send("PUT", $"/api/groups/{group}/objects/{objectId}/retention", """{"policy":{"keepLast":3}}""")).Body.GetRawText(),
        ];

        Assert.All(answers, a => Assert.DoesNotContain("hunter2-secret", a));
        Assert.Contains("hunter2-secret", File.ReadAllText(Path.Combine(_api.Root, "data", "state.json")));
    }

    [Fact]
    public async Task An_encrypted_object_needs_a_password_and_an_update_may_leave_it_empty_to_keep_it()
    {
        string group = await NewGroup();
        (HttpStatusCode missing, JsonElement body) = await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/x","encrypted":true}""");
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.Equal("object_password_required", body.GetProperty("code").GetString());

        string id = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/x","encrypted":true,"password":"pw"}""")).Body.GetProperty("id").GetString()!;
        Assert.True((await _api.Send("PUT", $"/api/groups/{group}/objects/{id}", """{"deviceId":"local","path":"/y","encrypted":true}""")).Body.GetProperty("hasPassword").GetBoolean());

        (HttpStatusCode off, JsonElement offBody) = await _api.Send("PUT", $"/api/groups/{group}/objects/{id}", """{"deviceId":"local","path":"/y","encrypted":false}""");
        Assert.Equal(HttpStatusCode.OK, off);
        Assert.False(offBody.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("object_password_required", (await _api.Send("PUT", $"/api/groups/{group}/objects/{id}", """{"deviceId":"local","path":"/y","encrypted":true}""")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_device_must_exist_and_a_cloud_drive_takes_encrypted_objects_only()
    {
        string group = await NewGroup();
        (HttpStatusCode noDevice, JsonElement noDeviceBody) = await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"nope","path":"/x","encrypted":false}""");
        Assert.Equal(HttpStatusCode.NotFound, noDevice);
        Assert.Equal("device_not_found", noDeviceBody.GetProperty("code").GetString());

        Assert.Equal("cloud_requires_encryption", (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"gdrive","path":"/x","encrypted":false}""")).Body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"gdrive","path":"/x","encrypted":true,"password":"pw"}""")).Status);
        Assert.Equal("path_invalid", (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"relative","encrypted":false}""")).Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_device_in_use_cannot_be_removed_until_its_objects_are()
    {
        string group = await NewGroup();
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");
        string id = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"media","path":"/x","encrypted":false}""")).Body.GetProperty("id").GetString()!;

        (HttpStatusCode status, JsonElement body) = await _api.Send("DELETE", "/api/devices/media");
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("device_in_use", body.GetProperty("code").GetString());

        await _api.Send("DELETE", $"/api/groups/{group}/objects/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.Send("DELETE", "/api/devices/media")).Status);
    }

    [Fact]
    public async Task Retention_is_set_on_an_encrypted_object_and_cleared_with_null()
    {
        string group = await NewGroup();
        string repo = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/r","encrypted":true,"password":"pw"}""")).Body.GetProperty("id").GetString()!;
        string plain = (await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/p","encrypted":false}""")).Body.GetProperty("id").GetString()!;

        JsonElement set = (await _api.Send("PUT", $"/api/groups/{group}/objects/{repo}/retention", """{"policy":{"keepLast":3,"keepMonthly":12}}""")).Body;
        Assert.Equal(3, set.GetProperty("retention").GetProperty("keepLast").GetInt32());
        Assert.Equal(12, set.GetProperty("retention").GetProperty("keepMonthly").GetInt32());

        Assert.Equal("retention_empty", (await _api.Send("PUT", $"/api/groups/{group}/objects/{repo}/retention", """{"policy":{}}""")).Body.GetProperty("code").GetString());
        Assert.Equal("retention_needs_repository", (await _api.Send("PUT", $"/api/groups/{group}/objects/{plain}/retention", """{"policy":{"keepLast":1}}""")).Body.GetProperty("code").GetString());

        JsonElement cleared = (await _api.Send("PUT", $"/api/groups/{group}/objects/{repo}/retention", """{"policy":null}""")).Body;
        Assert.False(cleared.TryGetProperty("retention", out _));
    }

    [Fact]
    public async Task The_state_survives_a_restart_of_the_server()
    {
        string group = await NewGroup("Kept");
        await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/kept","encrypted":true,"password":"pw"}""");

        using ApiFactory second = new();
        Directory.CreateDirectory(Path.Combine(second.Root, "data"));
        File.Copy(Path.Combine(_api.Root, "data", "state.json"), Path.Combine(second.Root, "data", "state.json"));

        JsonElement list = (await second.Send("GET", "/api/groups")).Body;
        Assert.Equal("Kept", list[0].GetProperty("name").GetString());
        Assert.Equal("/kept", list[0].GetProperty("objects")[0].GetProperty("path").GetString());
    }
}

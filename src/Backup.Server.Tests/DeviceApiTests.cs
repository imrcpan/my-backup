using System.Net;
using System.Text.Json;
using Backup.Infra.Disks;
using Xunit;

namespace Backup.Server.Tests;

public sealed class DeviceApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_new_installation_has_the_local_device_then_google_drive()
    {
        (HttpStatusCode status, JsonElement body) = await _api.Send("GET", "/api/devices");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["local", "gdrive"], body.EnumerateArray().Select(d => d.GetProperty("id").GetString()));
        Assert.All(body.EnumerateArray(), d => Assert.True(d.GetProperty("isBuiltIn").GetBoolean()));
        Assert.Equal("Local", body[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_disk_is_added_changed_and_removed_and_the_list_is_kept_in_the_file()
    {
        (HttpStatusCode created, JsonElement disk) = await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media disk","kind":"ExternalDisk","uuid":"1234-ABCD"}""");
        Assert.Equal(HttpStatusCode.OK, created);
        Assert.Equal("1234-ABCD", disk.GetProperty("uuid").GetString());
        Assert.False(disk.GetProperty("isBuiltIn").GetBoolean());

        (_, JsonElement renamed) = await _api.Send("PUT", "/api/devices/media", """{"name":"Photos disk","kind":"ExternalDisk","uuid":"FFFF-0000"}""");
        Assert.Equal("Photos disk", renamed.GetProperty("name").GetString());
        Assert.Equal("media", renamed.GetProperty("id").GetString());
        Assert.Contains("FFFF-0000", File.ReadAllText(Path.Combine(_api.Root, "data", "state.json")));

        Assert.Equal(HttpStatusCode.NoContent, (await _api.Send("DELETE", "/api/devices/media")).Status);
        (_, JsonElement list) = await _api.Send("GET", "/api/devices");
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Rules_come_back_as_codes_with_fitting_statuses()
    {
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");

        (HttpStatusCode taken, JsonElement takenBody) = await _api.Send("POST", "/api/devices", """{"id":"media","name":"Other","kind":"ExternalDisk","uuid":"1234-ABCD"}""");
        Assert.Equal(HttpStatusCode.Conflict, taken);
        Assert.Equal("device_id_taken", takenBody.GetProperty("code").GetString());

        (HttpStatusCode name, JsonElement nameBody) = await _api.Send("POST", "/api/devices", """{"id":"x","name":"media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");
        Assert.Equal(HttpStatusCode.Conflict, name);
        Assert.Equal("device_name_taken", nameBody.GetProperty("code").GetString());

        (HttpStatusCode bad, JsonElement badBody) = await _api.Send("POST", "/api/devices", """{"id":"Bad Id","name":"X","kind":"Local"}""");
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("device_id_invalid", badBody.GetProperty("code").GetString());

        (HttpStatusCode builtin, JsonElement builtinBody) = await _api.Send("DELETE", "/api/devices/local");
        Assert.Equal(HttpStatusCode.BadRequest, builtin);
        Assert.Equal("device_builtin", builtinBody.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("DELETE", "/api/devices/nope")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("GET", "/api/devices/nope/status")).Status);
    }

    [Fact]
    public async Task A_disk_reports_whether_it_is_connected_and_mounted()
    {
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");

        _api.Probe.Status = new DiskStatus(DiskState.NotConnected);
        Assert.Equal("not_connected", (await _api.Send("GET", "/api/devices/media/status")).Body.GetProperty("state").GetString());

        _api.Probe.Status = new DiskStatus(DiskState.NotMounted, "/dev/sdb1");
        Assert.Equal("not_mounted", (await _api.Send("GET", "/api/devices/media/status")).Body.GetProperty("state").GetString());

        _api.Probe.Status = new DiskStatus(DiskState.Mounted, "/dev/sdb1", "/run/media/x", ReadOnly: true);
        JsonElement mounted = (await _api.Send("GET", "/api/devices/media/status")).Body;
        Assert.Equal("mounted", mounted.GetProperty("state").GetString());
        Assert.Equal("/run/media/x", mounted.GetProperty("mountPoint").GetString());
        Assert.True(mounted.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Local_is_ok_and_a_cloud_drive_is_checked_through_rclone()
    {
        Assert.Equal("ok", (await _api.Send("GET", "/api/devices/local/status")).Body.GetProperty("state").GetString());
        Assert.Equal("ok", (await _api.Send("GET", "/api/devices/gdrive/status")).Body.GetProperty("state").GetString());

        await _api.Send("POST", "/api/devices", """{"id":"other","name":"Other drive","kind":"Rclone","rcloneRemote":"missing"}""");
        Assert.Equal("not_configured", (await _api.Send("GET", "/api/devices/other/status")).Body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Directories_can_be_browsed_on_every_kind_of_device()
    {
        Directory.CreateDirectory(Path.Combine(_api.Mount, "photos/2024"));
        Directory.CreateDirectory(Path.Combine(_api.Mount, "Music"));
        File.WriteAllText(Path.Combine(_api.Mount, "file.txt"), "x");
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");

        JsonElement top = (await _api.Send("GET", "/api/devices/media/directories")).Body;
        Assert.Equal(["Music", "photos"], top.EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.Equal(["/Music", "/photos"], top.EnumerateArray().Select(d => d.GetProperty("path").GetString()));

        JsonElement sub = (await _api.Send("GET", "/api/devices/media/directories?path=/photos/")).Body;
        Assert.Equal("/photos/2024", sub[0].GetProperty("path").GetString());

        Directory.CreateDirectory(Path.Combine(_api.Cloud, "backups"));
        JsonElement cloud = (await _api.Send("GET", $"/api/devices/gdrive/directories?path={Uri.EscapeDataString(_api.Cloud)}")).Body;
        Assert.Equal(["backups"], cloud.EnumerateArray().Select(d => d.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Browsing_a_disk_that_is_not_there_or_a_path_that_is_not_there_is_an_error()
    {
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"1234-ABCD"}""");

        (HttpStatusCode missing, JsonElement body) = await _api.Send("GET", "/api/devices/media/directories?path=/nothing");
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal("path_not_found", body.GetProperty("code").GetString());

        _api.Probe.Status = new DiskStatus(DiskState.NotConnected);
        Assert.Equal("disk_not_connected", (await _api.Send("GET", "/api/devices/media/directories")).Body.GetProperty("code").GetString());

        Assert.Equal("path_invalid", (await _api.Send("GET", "/api/devices/local/directories?path=relative")).Body.GetProperty("code").GetString());
    }
}

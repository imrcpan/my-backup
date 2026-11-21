using System.Text.Json;
using Backup.Infra.Disks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backup.Server.Tests;

public sealed class DiskApiTests : IDisposable
{
    private sealed class FakeLister : IDiskLister
    {
        public List<DiskInfo> Disks { get; } = [];

        public Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiskInfo>>([.. Disks]);
    }

    private readonly FakeLister _lister = new();
    private readonly ApiFactory _api;

    public DiskApiTests() => _api = new ApiFactory { Replace = services => services.AddSingleton<IDiskLister>(_lister) };

    public void Dispose() => _api.Dispose();

    private static DiskInfo Disk(string uuid, string device) => new(uuid, device, "Media", "ntfs", 1000, "WDC", "usb", true, null);

    [Fact]
    public async Task The_attached_disks_are_listed_each_time_so_a_disk_plugged_in_later_shows_up()
    {
        Assert.Equal(0, (await _api.Send("GET", "/api/disks")).Body.GetArrayLength());

        _lister.Disks.Add(Disk("65D1AEB5D3D9ECD5", "/dev/sda1"));
        JsonElement disks = (await _api.Send("GET", "/api/disks")).Body;

        JsonElement disk = Assert.Single(disks.EnumerateArray());
        Assert.Equal("65D1AEB5D3D9ECD5", disk.GetProperty("uuid").GetString());
        Assert.Equal("/dev/sda1", disk.GetProperty("device").GetString());
        Assert.Equal("Media", disk.GetProperty("label").GetString());
        Assert.Equal(1000, disk.GetProperty("sizeBytes").GetInt64());
        Assert.True(disk.GetProperty("removable").GetBoolean());
        Assert.False(disk.TryGetProperty("deviceId", out _));
    }

    [Fact]
    public async Task A_disk_that_is_a_device_already_says_which_one_ignoring_case()
    {
        _lister.Disks.Add(Disk("65D1AEB5D3D9ECD5", "/dev/sda1"));
        _lister.Disks.Add(Disk("AAAA-BBBB", "/dev/sdb1"));
        await _api.Send("POST", "/api/devices", """{"id":"media","name":"Media","kind":"ExternalDisk","uuid":"65d1aeb5d3d9ecd5"}""");

        JsonElement disks = (await _api.Send("GET", "/api/disks")).Body;

        Assert.Equal("media", disks[0].GetProperty("deviceId").GetString());
        Assert.False(disks[1].TryGetProperty("deviceId", out _));
    }
}

using Xunit;

namespace Backup.Core.Tests;

public class StorageDeviceCatalogTests
{
    private static StorageDevice Disk(string id = "media", string name = "Media disk", string uuid = "1234-ABCD") =>
        new(id, name, StorageDeviceKind.ExternalDisk, Uuid: uuid);

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void New_catalog_has_local_then_google_drive()
    {
        StorageDeviceCatalog catalog = new();
        Assert.Equal(["local", "gdrive"], catalog.Devices.Select(d => d.Id));
    }

    [Fact]
    public void Add_trims_and_keeps_the_device()
    {
        StorageDeviceCatalog catalog = new();
        StorageDevice added = catalog.Add(Disk(" media ", " Media disk ", " 1234-ABCD "));
        Assert.Equal("media", added.Id);
        Assert.Equal("Media disk", added.Name);
        Assert.Equal("1234-ABCD", added.Uuid);
        Assert.Same(added, catalog.Find("media"));
    }

    [Fact]
    public void Id_and_name_are_unique()
    {
        StorageDeviceCatalog catalog = new();
        catalog.Add(Disk());
        Assert.Equal("device_id_taken", CodeOf(() => catalog.Add(Disk(name: "Other"))));
        Assert.Equal("device_name_taken", CodeOf(() => catalog.Add(Disk(id: "other", name: "media DISK"))));
        Assert.Equal("device_name_taken", CodeOf(() => catalog.Add(Disk(id: "other", name: "Local"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("-lead")]
    public void Bad_ids_are_rejected(string id)
    {
        Assert.Equal("device_id_invalid", CodeOf(() => new StorageDeviceCatalog().Add(Disk(id: id))));
    }

    [Fact]
    public void External_disk_needs_uuid_and_cloud_needs_remote()
    {
        StorageDeviceCatalog catalog = new();
        Assert.Equal("device_uuid_invalid", CodeOf(() => catalog.Add(new("d", "D", StorageDeviceKind.ExternalDisk))));
        Assert.Equal("device_remote_invalid", CodeOf(() => catalog.Add(new("c", "C", StorageDeviceKind.Rclone))));
        Assert.Equal("remote", catalog.Add(new("c", "C", StorageDeviceKind.Rclone, RcloneRemote: "remote:")).RcloneRemote);
    }

    [Fact]
    public void Update_may_replace_the_disk_but_keeps_the_id_for_built_ins()
    {
        StorageDeviceCatalog catalog = new();
        catalog.Add(Disk());
        catalog.Update("media", Disk(uuid: "FFFF-0000"));
        Assert.Equal("FFFF-0000", catalog.Find("media")!.Uuid);

        Assert.Equal("device_builtin", CodeOf(() => catalog.Update("local", StorageDevice.Local with { Id = "x" })));
        catalog.Update("gdrive", StorageDevice.GoogleDrive with { RcloneRemote = "mydrive" });
        Assert.Equal("mydrive", catalog.Find("gdrive")!.RcloneRemote);
    }

    [Fact]
    public void Built_in_devices_cannot_be_removed_but_others_can()
    {
        StorageDeviceCatalog catalog = new();
        catalog.Add(Disk());
        catalog.Remove("media");
        Assert.Null(catalog.Find("media"));
        Assert.Equal("device_builtin", CodeOf(() => catalog.Remove("local")));
        Assert.Equal("device_not_found", CodeOf(() => catalog.Remove("media")));
    }

    [Fact]
    public void Saved_devices_are_loaded_over_the_defaults()
    {
        StorageDeviceCatalog catalog = new([StorageDevice.GoogleDrive with { RcloneRemote = "mydrive" }, Disk()]);
        Assert.Equal(["local", "gdrive", "media"], catalog.Devices.Select(d => d.Id));
        Assert.Equal("mydrive", catalog.Find("gdrive")!.RcloneRemote);
    }
}

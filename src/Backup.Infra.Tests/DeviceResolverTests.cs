using Backup.Core;
using Backup.Infra.Disks;
using Backup.Infra.Storage;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class DeviceResolverTests : IDisposable
{
    private sealed class FakeProbe(DiskStatus status) : IDiskProbe
    {
        public string? Asked { get; private set; }

        public DiskStatus Check(string uuid)
        {
            Asked = uuid;
            return status;
        }
    }

    private readonly string _root = Directory.CreateTempSubdirectory("resolver").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static StorageObject Obj(string path, string device = "disk") => new("o1", device, path, false, null);

    private static StorageDevice Disk => new("disk", "Media disk", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD");

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void Local_and_cloud_objects_resolve_without_a_probe()
    {
        DeviceResolver resolver = new(new FakeProbe(new DiskStatus(DiskState.NotConnected)));
        ResolvedLocation local = resolver.Resolve(Obj("/home/me", "local"), StorageDevice.Local, true);
        Assert.Equal("/home/me", local.FileSystemPath);
        Assert.Equal("/home/me", local.ResticLocation);

        ResolvedLocation cloud = resolver.Resolve(Obj("/backups/docs", "gdrive"), StorageDevice.GoogleDrive, true);
        Assert.Null(cloud.FileSystemPath);
        Assert.Equal("gdrive:/backups/docs", cloud.RcloneSpec);
        Assert.Equal("rclone:gdrive:/backups/docs", cloud.ResticLocation);
    }

    [Fact]
    public void Mounted_disk_gives_a_path_below_its_mount_point()
    {
        FakeProbe probe = new(new DiskStatus(DiskState.Mounted, "/dev/sdb1", _root));
        DeviceResolver resolver = new(probe);
        Assert.Equal(Path.Combine(_root, "photos/2024"), resolver.Resolve(Obj("/photos/2024"), Disk, false).FileSystemPath);
        Assert.Equal(_root, resolver.Resolve(Obj("/"), Disk, false).FileSystemPath);
        Assert.Equal("1234-ABCD", probe.Asked);
    }

    [Fact]
    public void A_disk_that_is_not_there_is_refused_with_a_reason()
    {
        Assert.Equal("disk_not_connected", CodeOf(() => new DeviceResolver(new FakeProbe(new DiskStatus(DiskState.NotConnected))).Resolve(Obj("/x"), Disk, false)));
        Assert.Equal("disk_not_mounted", CodeOf(() => new DeviceResolver(new FakeProbe(new DiskStatus(DiskState.NotMounted, "/dev/sdb1"))).Resolve(Obj("/x"), Disk, false)));
    }

    [Fact]
    public void A_read_only_disk_can_be_read_but_not_written()
    {
        DeviceResolver resolver = new(new FakeProbe(new DiskStatus(DiskState.Mounted, "/dev/sdb1", _root, ReadOnly: true)));
        Assert.NotNull(resolver.Resolve(Obj("/x"), Disk, forWrite: false));
        Assert.Equal("disk_read_only", CodeOf(() => resolver.Resolve(Obj("/x"), Disk, forWrite: true)));
    }

    [Fact]
    public void A_link_that_leads_out_of_the_disk_is_refused()
    {
        string outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        string mount = Directory.CreateDirectory(Path.Combine(_root, "mount")).FullName;
        File.CreateSymbolicLink(Path.Combine(mount, "escape"), outside);
        Directory.CreateDirectory(Path.Combine(mount, "real"));
        DeviceResolver resolver = new(new FakeProbe(new DiskStatus(DiskState.Mounted, "/dev/sdb1", mount)));

        Assert.Equal("path_escapes_device", CodeOf(() => resolver.Resolve(Obj("/escape"), Disk, false)));
        Assert.Equal("path_escapes_device", CodeOf(() => resolver.Resolve(Obj("/escape/new/deeper"), Disk, false)));
        Assert.Equal(Path.Combine(mount, "real"), resolver.Resolve(Obj("/real"), Disk, false).FileSystemPath);
        Assert.NotNull(resolver.Resolve(Obj("/not/created/yet"), Disk, true));
    }

    [Fact]
    public void Source_guard_refuses_missing_and_empty_directories()
    {
        string empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        Assert.Equal("source_missing", CodeOf(() => SourceGuard.EnsureUsable(Path.Combine(_root, "none"))));
        Assert.Equal("source_empty", CodeOf(() => SourceGuard.EnsureUsable(empty)));

        Directory.CreateDirectory(Path.Combine(empty, "sub"));
        SourceGuard.EnsureUsable(empty);
    }
}

using Backup.Infra.Disks;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class DiskListerTests : IDisposable
{
    private const string Lsblk = """
        {"blockdevices": [
          {"name":"sda","path":"/dev/sda","uuid":null,"label":null,"size":2000365289472,"fstype":null,"mountpoints":[],"rm":false,"hotplug":true,"tran":"usb","model":"WDC WD20SDRW ",
           "children":[
             {"name":"sda1","path":"/dev/sda1","uuid":"65D1AEB5D3D9ECD5","label":"Media","size":2000363192320,"fstype":"ntfs","mountpoints":[null,"/run/media/prc/Media"],"rm":false,"hotplug":true,"tran":null,"model":null},
             {"name":"sda2","path":"/dev/sda2","uuid":"AAAA-BBBB","label":null,"size":1000,"fstype":"vfat","mountpoints":[],"rm":false,"hotplug":true,"tran":null,"model":null}]},
          {"name":"nvme0n1","path":"/dev/nvme0n1","uuid":null,"size":512000000000,"fstype":null,"mountpoints":[],"rm":false,"hotplug":false,"tran":"nvme","model":"Samsung",
           "children":[
             {"name":"nvme0n1p1","path":"/dev/nvme0n1p1","uuid":"1111-2222","label":"EFI","size":600000000,"fstype":"vfat","mountpoints":["/boot/efi"],"rm":false,"hotplug":false,"tran":null,"model":null},
             {"name":"nvme0n1p2","path":"/dev/nvme0n1p2","uuid":"3333-luks","label":null,"size":1,"fstype":"crypto_LUKS","mountpoints":[],"rm":false,"hotplug":false,"tran":null,"model":null}]},
          {"name":"zram0","path":"/dev/zram0","uuid":"swap-uuid","label":"zram0","size":8589934592,"fstype":"swap","mountpoints":["[SWAP]"],"rm":false,"hotplug":false,"tran":null,"model":null}
        ]}
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("disklister").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task File_systems_are_listed_with_external_disks_first_and_what_is_not_a_file_system_is_left_out()
    {
        RecordingRunner runner = new();
        runner.Answer(0, Lsblk);

        IReadOnlyList<DiskInfo> disks = await new DiskLister(runner).ListAsync(Ct);

        Assert.Equal(["65D1AEB5D3D9ECD5", "AAAA-BBBB", "1111-2222"], disks.Select(d => d.Uuid));
        DiskInfo media = disks[0];
        Assert.Equal("/dev/sda1", media.Device);
        Assert.Equal("Media", media.Label);
        Assert.Equal("ntfs", media.FileSystem);
        Assert.Equal(2000363192320, media.SizeBytes);
        Assert.Equal("WDC WD20SDRW", media.Model);
        Assert.Equal("usb", media.Transport);
        Assert.True(media.Removable);
        Assert.Equal("/run/media/prc/Media", media.MountPoint);
        Assert.Null(disks[1].MountPoint);
        Assert.False(disks[2].Removable);
        Assert.Equal("/boot/efi", disks[2].MountPoint);
    }

    [Fact]
    public async Task Older_lsblk_writes_flags_and_the_mount_point_in_its_own_way()
    {
        RecordingRunner runner = new();
        runner.Answer(1, "", "lsblk: unknown column: MOUNTPOINTS");
        runner.Answer(0, """{"blockdevices":[{"name":"sdb1","uuid":"U-1","fstype":"ext4","size":"4096","rm":"1","hotplug":"0","mountpoint":"/mnt/x"}]}""");

        DiskInfo disk = Assert.Single(await new DiskLister(runner).ListAsync(Ct));

        Assert.True(disk.Removable);
        Assert.Equal(4096, disk.SizeBytes);
        Assert.Equal("/mnt/x", disk.MountPoint);
        Assert.Equal("/dev/sdb1", disk.Device);
        Assert.Contains("MOUNTPOINTS", runner.Specs[0].Arguments[3]);
        Assert.Contains("MOUNTPOINT,", runner.Specs[1].Arguments[3]);
    }

    [Fact]
    public async Task Without_lsblk_the_links_by_uuid_are_used()
    {
        string byUuid = Path.Combine(_root, "by-uuid");
        Directory.CreateDirectory(byUuid);
        string dev = Directory.CreateDirectory(Path.Combine(_root, "dev")).FullName;
        File.WriteAllText(Path.Combine(dev, "sdb1"), "");
        File.CreateSymbolicLink(Path.Combine(byUuid, "1234-ABCD"), Path.Combine("..", "dev", "sdb1"));
        string info = Path.Combine(_root, "mountinfo");
        File.WriteAllText(info, $"40 25 8:17 / /mnt/disk rw - ext4 {dev}/sdb1 rw\n");

        DiskLister lister = new(new Backup.Infra.Process.ProcessRunner(), "/no/such/lsblk", byUuid, info);
        DiskInfo disk = Assert.Single(await lister.ListAsync(Ct));

        Assert.Equal("1234-ABCD", disk.Uuid);
        Assert.Equal("/mnt/disk", disk.MountPoint);
        Assert.Null(disk.Label);
    }

    [Fact]
    public async Task Nothing_attached_gives_an_empty_list()
    {
        RecordingRunner runner = new();
        runner.Answer(0, """{"blockdevices":[]}""");
        Assert.Empty(await new DiskLister(runner).ListAsync(Ct));
        DiskLister none = new(new Backup.Infra.Process.ProcessRunner(), "/no/such/lsblk", Path.Combine(_root, "none"), Path.Combine(_root, "none"));
        Assert.Empty(await none.ListAsync(Ct));
    }

    [Fact]
    public async Task The_real_lsblk_of_this_computer_gives_a_list_without_failing()
    {
        IReadOnlyList<DiskInfo> disks = await new DiskLister(new Backup.Infra.Process.ProcessRunner()).ListAsync(Ct);
        Assert.All(disks, d => Assert.False(string.IsNullOrEmpty(d.Uuid)));
    }
}

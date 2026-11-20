using Backup.Infra.Disks;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class DiskProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("diskprobe").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private DiskProbe Probe(string[] mountInfo, string? uuid = "1234-ABCD", string device = "sdb1")
    {
        string byUuid = Path.Combine(_root, "by-uuid");
        Directory.CreateDirectory(byUuid);
        string dev = Path.Combine(_root, "dev");
        Directory.CreateDirectory(dev);
        File.WriteAllText(Path.Combine(dev, device), "");
        if (uuid != null)
        {
            File.CreateSymbolicLink(Path.Combine(byUuid, uuid), Path.Combine("..", "dev", device));
        }

        string info = Path.Combine(_root, "mountinfo");
        File.WriteAllLines(info, mountInfo.Select(l => l.Replace("{dev}", dev)));
        return new DiskProbe(byUuid, info);
    }

    [Fact]
    public void Disk_without_a_uuid_link_is_not_connected()
    {
        Assert.Equal(DiskState.NotConnected, Probe([], uuid: null).Check("1234-ABCD").State);
    }

    [Fact]
    public void Attached_disk_without_a_mount_is_not_mounted()
    {
        DiskStatus status = Probe(["25 1 8:1 / / rw - ext4 {dev}/sda1 rw"]).Check("1234-ABCD");
        Assert.Equal(DiskState.NotMounted, status.State);
        Assert.EndsWith("sdb1", status.DevicePath);
    }

    [Fact]
    public void Mounted_disk_gives_its_mount_point()
    {
        DiskStatus status = Probe(["40 25 8:17 / /run/media/prc/MEDIA rw,relatime - exfat {dev}/sdb1 rw"]).Check("1234-ABCD");
        Assert.Equal(DiskState.Mounted, status.State);
        Assert.Equal("/run/media/prc/MEDIA", status.MountPoint);
        Assert.False(status.ReadOnly);
    }

    [Fact]
    public void Uuid_is_compared_ignoring_case()
    {
        Assert.Equal(DiskState.Mounted, Probe(["40 25 8:17 / /mnt/m rw - ext4 {dev}/sdb1 rw"], "abcd-1234").Check("ABCD-1234").State);
    }

    [Fact]
    public void Read_only_mount_is_reported_and_whole_mount_is_preferred_over_a_bind_of_a_subdirectory()
    {
        DiskStatus status = Probe([
            "41 25 8:17 /photos /home/prc/photos rw - ext4 {dev}/sdb1 rw",
            "40 25 8:17 / /mnt/m ro,relatime - ext4 {dev}/sdb1 rw",
        ]).Check("1234-ABCD");
        Assert.Equal("/mnt/m", status.MountPoint);
        Assert.True(status.ReadOnly);
    }

    [Fact]
    public void Another_disk_mounted_elsewhere_does_not_count()
    {
        DiskStatus status = Probe(["40 25 8:33 / /mnt/other rw - ext4 {dev}/sdc1 rw"]).Check("1234-ABCD");
        Assert.Equal(DiskState.NotMounted, status.State);
    }

    [Fact]
    public void Missing_by_uuid_directory_means_not_connected()
    {
        Assert.Equal(DiskState.NotConnected, new DiskProbe(Path.Combine(_root, "none"), Path.Combine(_root, "none")).Check("x").State);
    }

    [Fact]
    public void Escaped_characters_in_mount_points_are_decoded()
    {
        DiskProbe.MountEntry entry = Assert.Single(DiskProbe.ParseMountInfo(["40 25 8:17 / /mnt/My\\040Disk rw - ext4 /dev/sdb1 rw"]));
        Assert.Equal("/mnt/My Disk", entry.MountPoint);
        Assert.Equal("a\\b", DiskProbe.Unescape("a\\134b"));
        Assert.Equal("bad\\9", DiskProbe.Unescape("bad\\9"));
    }

    [Fact]
    public void Malformed_lines_are_skipped()
    {
        Assert.Empty(DiskProbe.ParseMountInfo(["", "garbage", "a b - c"]));
    }
}

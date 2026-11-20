namespace Backup.Infra.Disks;

public enum DiskState
{
    /// <summary>No disk with this UUID is attached.</summary>
    NotConnected,

    /// <summary>The disk is attached but its file system is not mounted.</summary>
    NotMounted,

    /// <summary>The disk is attached and mounted at <see cref="DiskStatus.MountPoint"/>.</summary>
    Mounted,
}

public sealed record DiskStatus(DiskState State, string? DevicePath = null, string? MountPoint = null, bool ReadOnly = false);

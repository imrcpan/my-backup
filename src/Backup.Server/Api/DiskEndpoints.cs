using Backup.Core;
using Backup.Infra.Disks;

namespace Backup.Server.Api;

/// <param name="DeviceId">The id of the storage device that already has this UUID, if there is one.</param>
public sealed record DiskDto(
    string Uuid,
    string Device,
    string? Label,
    string? FileSystem,
    long? SizeBytes,
    string? Model,
    string? Transport,
    bool Removable,
    string? MountPoint,
    string? DeviceId);

public static class DiskEndpoints
{
    /// <summary>The disks that are attached now, to choose from when an external disk is added. It is read again on every call, as a disk may be plugged in at any time.</summary>
    public static void Map(WebApplication app) => app.MapGet("/api/disks", async (IDiskLister lister, Workspace workspace, CancellationToken cancellationToken) =>
    {
        IReadOnlyList<DiskInfo> disks = await lister.ListAsync(cancellationToken);
        IReadOnlyList<StorageDevice> devices = workspace.Read((known, _) => known.Devices.ToArray());
        return disks.Select(disk => new DiskDto(
            disk.Uuid, disk.Device, disk.Label, disk.FileSystem, disk.SizeBytes, disk.Model, disk.Transport, disk.Removable, disk.MountPoint,
            devices.FirstOrDefault(d => d.Uuid != null && string.Equals(d.Uuid, disk.Uuid, StringComparison.OrdinalIgnoreCase))?.Id)).ToArray();
    });
}

using Backup.Core;
using Backup.Infra.Disks;

namespace Backup.Infra.Storage;

/// <summary>Where a storage object really is, once its device was found to be usable.</summary>
/// <param name="FileSystemPath">An absolute path, for a local directory or a mounted disk; null on a cloud drive.</param>
/// <param name="RcloneRemote">The rclone remote, for a cloud drive; null otherwise.</param>
/// <param name="RclonePath">The path on the remote, with a leading slash.</param>
public sealed record ResolvedLocation(StorageObject Object, StorageDevice Device, string? FileSystemPath, string? RcloneRemote, string? RclonePath)
{
    /// <summary>The form that restic and rclone take: a path, or "remote:/path".</summary>
    public string RcloneSpec => RcloneRemote == null ? FileSystemPath! : $"{RcloneRemote}:{RclonePath}";

    /// <summary>The form of a restic repository location.</summary>
    public string ResticLocation => RcloneRemote == null ? FileSystemPath! : $"rclone:{RcloneRemote}:{RclonePath}";
}

public interface IDeviceResolver
{
    /// <summary>
    /// Finds the place of an object, and refuses if its device cannot be trusted right now:
    /// a disk that is not attached, not mounted, or mounted read-only when it is to be written.
    /// </summary>
    /// <exception cref="DomainException">"disk_not_connected", "disk_not_mounted", "disk_read_only" or "path_escapes_device".</exception>
    ResolvedLocation Resolve(StorageObject item, StorageDevice device, bool forWrite);
}

public sealed class DeviceResolver(IDiskProbe diskProbe) : IDeviceResolver
{
    public ResolvedLocation Resolve(StorageObject item, StorageDevice device, bool forWrite)
    {
        switch (device.Kind)
        {
            case StorageDeviceKind.Local:
                return new ResolvedLocation(item, device, item.Path, null, null);

            case StorageDeviceKind.Rclone:
                return new ResolvedLocation(item, device, null, device.RcloneRemote!, item.Path);

            default:
                DiskStatus status = diskProbe.Check(device.Uuid!);
                if (status.State == DiskState.NotConnected)
                {
                    throw new DomainException("disk_not_connected", $"The disk '{device.Name}' is not connected.");
                }

                if (status.State == DiskState.NotMounted)
                {
                    throw new DomainException("disk_not_mounted", $"The disk '{device.Name}' is connected but not mounted.");
                }

                if (forWrite && status.ReadOnly)
                {
                    throw new DomainException("disk_read_only", $"The disk '{device.Name}' is mounted read-only.");
                }

                string path = item.Path == "/" ? status.MountPoint! : status.MountPoint!.TrimEnd('/') + item.Path;
                EnsureInside(status.MountPoint!, path, device);
                return new ResolvedLocation(item, device, path, null, null);
        }
    }

    /// <summary>A symbolic link inside the disk must not lead the path out of it.</summary>
    private static void EnsureInside(string mountPoint, string path, StorageDevice device)
    {
        string root = RealPath(mountPoint);
        string real = RealPath(path);
        if (real != root && !real.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal))
        {
            throw new DomainException("path_escapes_device", $"The path leaves the disk '{device.Name}'.");
        }
    }

    /// <summary>The path with links resolved, as far as it exists.</summary>
    private static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string existing = full;
        List<string> rest = [];
        while (!Path.Exists(existing) && Path.GetDirectoryName(existing) is string parent)
        {
            rest.Insert(0, Path.GetFileName(existing));
            existing = parent;
        }

        string resolved = new DirectoryInfo(existing).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? ResolveParents(existing);
        return Path.Combine([resolved, .. rest]);
    }

    private static string ResolveParents(string path)
    {
        // Links in the parents are resolved one component at a time.
        string current = "/";
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is FileSystemInfo target)
            {
                current = target.FullName;
            }
        }

        return current;
    }
}

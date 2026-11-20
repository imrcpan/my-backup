using Backup.Core;
using Backup.Infra.Disks;
using Backup.Infra.Rclone;
using Backup.Infra.Storage;

namespace Backup.Server.Api;

public sealed record DeviceDto(string Id, string Name, StorageDeviceKind Kind, string? Uuid, string? RcloneRemote, bool IsBuiltIn);

public sealed record DeviceRequest(string? Id, string? Name, StorageDeviceKind Kind, string? Uuid, string? RcloneRemote);

/// <param name="State">"ok", "not_connected", "not_mounted", "mounted", "not_configured", "unreachable".</param>
public sealed record DeviceStatusDto(string State, string? Message = null, string? MountPoint = null, bool ReadOnly = false);

public sealed record DirectoryDto(string Name, string Path);

public static class DeviceEndpoints
{
    public static DeviceDto ToDto(StorageDevice device) =>
        new(device.Id, device.Name, device.Kind, device.Uuid, device.RcloneRemote, device.IsBuiltIn);

    public static void Map(WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/devices");

        api.MapGet("/", (Workspace workspace) =>
            workspace.Read((devices, _) => devices.Devices.Select(ToDto).ToArray()));

        api.MapPost("/", (DeviceRequest request, Workspace workspace) => workspace.Write((devices, _) =>
            ToDto(devices.Add(new StorageDevice(request.Id ?? "", request.Name ?? "", request.Kind, request.Uuid, request.RcloneRemote)))));

        api.MapPut("/{id}", (string id, DeviceRequest request, Workspace workspace) =>
            ToDto(workspace.UpdateDevice(id, new StorageDevice(request.Id ?? id, request.Name ?? "", request.Kind, request.Uuid, request.RcloneRemote))));

        api.MapDelete("/{id}", (string id, Workspace workspace) =>
        {
            workspace.RemoveDevice(id);
            return Results.NoContent();
        });

        api.MapGet("/{id}/status", async (string id, Workspace workspace, IDiskProbe probe, IRcloneClient rclone, CancellationToken cancellationToken) =>
        {
            StorageDevice device = Find(workspace, id);
            switch (device.Kind)
            {
                case StorageDeviceKind.Local:
                    return new DeviceStatusDto("ok");
                case StorageDeviceKind.ExternalDisk:
                    DiskStatus disk = probe.Check(device.Uuid!);
                    return disk.State switch
                    {
                        DiskState.NotConnected => new DeviceStatusDto("not_connected"),
                        DiskState.NotMounted => new DeviceStatusDto("not_mounted"),
                        _ => new DeviceStatusDto("mounted", MountPoint: disk.MountPoint, ReadOnly: disk.ReadOnly),
                    };
                default:
                    RemoteStatus remote = await rclone.CheckRemoteAsync(device.RcloneRemote!, cancellationToken);
                    return remote.State switch
                    {
                        RemoteState.Ready => new DeviceStatusDto("ok"),
                        RemoteState.NotConfigured => new DeviceStatusDto("not_configured"),
                        _ => new DeviceStatusDto("unreachable", remote.Message),
                    };
            }
        });

        // rclone opens a browser for the user to sign in to Google and waits, so this ends when they finish, or when the request is dropped.
        api.MapPost("/{id}/authorize", async (string id, Workspace workspace, IRcloneClient rclone, CancellationToken cancellationToken) =>
        {
            StorageDevice device = Find(workspace, id);
            if (device.Kind != StorageDeviceKind.Rclone)
            {
                throw new DomainException("device_not_cloud", "Only a cloud drive can be signed in to.");
            }

            await rclone.CreateGoogleDriveRemoteAsync(device.RcloneRemote!, null, cancellationToken);
            return Results.NoContent();
        });

        // The directories below a path of the device, for choosing where a storage object goes.
        api.MapGet("/{id}/directories", async (string id, string? path, Workspace workspace, IDeviceResolver resolver, IRcloneClient rclone, CancellationToken cancellationToken) =>
        {
            StorageDevice device = Find(workspace, id);
            string normalized = StoragePath.Normalize(path ?? "/");
            ResolvedLocation location = resolver.Resolve(new StorageObject("browse", device.Id, normalized, false, null), device, forWrite: false);
            if (location.RcloneRemote != null)
            {
                IReadOnlyList<RcloneEntry> entries = await rclone.ListDirectoriesAsync(location.RcloneRemote, normalized, cancellationToken);
                return entries.Select(e => new DirectoryDto(e.Name, Join(normalized, e.Name))).ToArray();
            }

            string directory = location.FileSystemPath!;
            if (!Directory.Exists(directory))
            {
                throw new DomainException("path_not_found", "The directory does not exist.");
            }

            return new DirectoryInfo(directory)
                .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new DirectoryDto(d.Name, Join(normalized, d.Name)))
                .ToArray();
        });
    }

    private static string Join(string parent, string name) => parent == "/" ? "/" + name : parent + "/" + name;

    internal static StorageDevice Find(Workspace workspace, string id) =>
        workspace.Read((devices, _) => devices.Find(id)) ?? throw new DomainException("device_not_found", "The storage device does not exist.");
}

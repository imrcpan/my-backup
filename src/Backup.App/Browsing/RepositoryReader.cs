using Backup.Core;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Browsing;

/// <summary>Reads what is in a repository, to show snapshots and their files. It changes nothing.</summary>
public sealed class RepositoryReader(Workspace workspace, IDeviceResolver resolver, IResticClient restic, IRcloneClient rclone)
{
    public async Task<IReadOnlyList<ResticSnapshot>> SnapshotsAsync(string groupId, string objectId, CancellationToken cancellationToken = default)
    {
        ResticRepository repository = await OpenAsync(groupId, objectId, cancellationToken);
        return await restic.SnapshotsAsync(repository, cancellationToken);
    }

    /// <summary>The direct children of a directory of a snapshot ("/" for the top).</summary>
    public async Task<IReadOnlyList<ResticNode>> FilesAsync(string groupId, string objectId, string snapshotId, string? directory, CancellationToken cancellationToken = default)
    {
        ResticRepository repository = await OpenAsync(groupId, objectId, cancellationToken);
        IReadOnlyList<ResticSnapshot> snapshots = await restic.SnapshotsAsync(repository, cancellationToken);
        ResticSnapshot[] found = [.. snapshots.Where(s => s.Id.StartsWith(snapshotId, StringComparison.OrdinalIgnoreCase))];
        if (snapshotId.Length < 8 || found.Length != 1)
        {
            throw new DomainException("snapshot_not_found", "The snapshot does not exist or the id is not exact.");
        }

        return await restic.ListAsync(repository, found[0].Id, StoragePath.Normalize(directory ?? "/"), cancellationToken);
    }

    private async Task<ResticRepository> OpenAsync(string groupId, string objectId, CancellationToken cancellationToken)
    {
        (StorageObject item, StorageDevice device) = workspace.Read((devices, groups) =>
        {
            StorageObject item = groups.Get(groupId).Objects.Find(o => o.Id == objectId)
                ?? throw new DomainException("object_not_found", "The storage object does not exist.");
            return (item, devices.Find(item.DeviceId) ?? throw new DomainException("device_not_found", "The storage device does not exist."));
        });
        if (!item.Encrypted)
        {
            throw new DomainException("object_not_a_repository", "Only an encrypted object holds snapshots.");
        }

        ResolvedLocation location = resolver.Resolve(item, device, forWrite: false);
        if (location.RcloneRemote != null)
        {
            RemoteStatus status = await rclone.CheckRemoteAsync(location.RcloneRemote, cancellationToken);
            if (status.State != RemoteState.Ready)
            {
                throw new DomainException(
                    status.State == RemoteState.NotConfigured ? "remote_not_configured" : "remote_unreachable",
                    status.Message ?? $"The remote '{location.RcloneRemote}' is not ready.");
            }
        }

        ResticRepository repository = new(location.ResticLocation, item.Password!);
        return await restic.ExistsAsync(repository, cancellationToken)
            ? repository
            : throw new DomainException("repo_missing", "There is no repository at this place yet.");
    }
}

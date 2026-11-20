namespace Backup.Infra.Restic;

public interface IResticClient
{
    Task<bool> ExistsAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    /// <exception cref="ExternalToolException">"repo_exists" if there is a repository already.</exception>
    Task InitAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResticSnapshot>> SnapshotsAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Lists the direct children of a directory of a snapshot ("/" for the top).</summary>
    Task<IReadOnlyList<ResticNode>> ListAsync(ResticRepository repository, string snapshotId, string directory, CancellationToken cancellationToken = default);
}

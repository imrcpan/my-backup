using Backup.Core;

namespace Backup.Infra.Restic;

public interface IResticClient
{
    Task<bool> ExistsAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    /// <exception cref="ExternalToolException">"repo_exists" if there is a repository already.</exception>
    Task InitAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResticSnapshot>> SnapshotsAsync(ResticRepository repository, CancellationToken cancellationToken = default);

    /// <summary>Lists the direct children of a directory of a snapshot ("/" for the top).</summary>
    Task<IReadOnlyList<ResticNode>> ListAsync(ResticRepository repository, string snapshotId, string directory, CancellationToken cancellationToken = default);

    /// <summary>Finds out what a backup of the directory would save, without saving anything.</summary>
    Task<ResticBackupPlan> PlanBackupAsync(ResticRepository repository, string sourceDirectory, CancellationToken cancellationToken = default);

    Task<ResticBackupSummary> BackupAsync(
        ResticRepository repository,
        string sourceDirectory,
        Action<ResticBackupProgress>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a snapshot, or only the given paths of it, below a new directory.
    /// Nothing is ever restored over existing files: the target must not exist or must be empty.
    /// </summary>
    /// <exception cref="ExternalToolException">"restore_target_not_empty" if the target holds anything.</exception>
    Task<ResticRestoreSummary> RestoreAsync(
        ResticRepository repository,
        string snapshotId,
        string targetDirectory,
        IReadOnlyList<string>? paths = null,
        Action<ResticRestoreProgress>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds which snapshots a policy would remove. Nothing is removed.</summary>
    Task<ResticForgetPlan> PlanForgetAsync(ResticRepository repository, RetentionPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>Removes the snapshots that the policy does not keep. Their data stays until <see cref="PruneAsync"/>.</summary>
    Task<ResticForgetPlan> ForgetAsync(ResticRepository repository, RetentionPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>Finds which of the given snapshots exist, to be removed by <see cref="ForgetSnapshotsAsync"/>.</summary>
    Task<ResticForgetPlan> PlanForgetSnapshotsAsync(ResticRepository repository, IReadOnlyList<string> snapshotIds, CancellationToken cancellationToken = default);

    Task ForgetSnapshotsAsync(ResticRepository repository, IReadOnlyList<string> snapshotIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes the data that no snapshot uses any more. This frees space and cannot be undone.</summary>
    Task PruneAsync(ResticRepository repository, Action<string>? onLine = null, CancellationToken cancellationToken = default);
}

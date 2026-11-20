using Backup.Core;
using Backup.Infra.Restic;

namespace Backup.App.Tests;

/// <summary>A restic that only does what a test sets up; everything else is a mistake of the test.</summary>
public sealed class FakeRestic : IResticClient
{
    public Func<ResticRepository, string, Action<ResticBackupProgress>?, CancellationToken, Task<ResticBackupSummary>> OnBackup { get; set; } =
        (_, _, _, _) => throw new NotSupportedException();

    public int Backups { get; private set; }

    public Task<bool> ExistsAsync(ResticRepository repository, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<ResticBackupPlan> PlanBackupAsync(ResticRepository repository, string sourceDirectory, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResticBackupPlan(1, 0, 0, 5, 5, [new ResticPlanItem("new", sourceDirectory + "/a.txt", 5)], false));

    public Task<ResticBackupSummary> BackupAsync(ResticRepository repository, string sourceDirectory, Action<ResticBackupProgress>? onProgress = null, CancellationToken cancellationToken = default)
    {
        Backups++;
        return OnBackup(repository, sourceDirectory, onProgress, cancellationToken);
    }

    public Task InitAsync(ResticRepository repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ResticSnapshot>> SnapshotsAsync(ResticRepository repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ResticNode>> ListAsync(ResticRepository repository, string snapshotId, string directory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResticRestoreSummary> RestoreAsync(ResticRepository repository, string snapshotId, string targetDirectory, IReadOnlyList<string>? paths = null, Action<ResticRestoreProgress>? onProgress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResticForgetPlan> PlanForgetAsync(ResticRepository repository, RetentionPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResticForgetPlan> ForgetAsync(ResticRepository repository, RetentionPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResticForgetPlan> PlanForgetSnapshotsAsync(ResticRepository repository, IReadOnlyList<string> snapshotIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ForgetSnapshotsAsync(ResticRepository repository, IReadOnlyList<string> snapshotIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task PruneAsync(ResticRepository repository, Action<string>? onLine = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResticCheckResult> CheckAsync(ResticRepository repository, ResticCheckMode mode, int subsetPercent = 10, Action<string>? onLine = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task InitFromAsync(ResticRepository repository, ResticRepository source, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task CopyAsync(ResticRepository repository, ResticRepository source, string snapshotId, Action<string>? onLine = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

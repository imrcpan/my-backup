namespace Backup.Infra.Restic;

/// <param name="Action">"new" or "modified".</param>
public sealed record ResticPlanItem(string Action, string Path, long Size);

/// <summary>What a backup would do now, found by a dry run that writes nothing.</summary>
/// <param name="Items">The files to be saved; cut to the first <see cref="ResticClient.MaxPlanItems"/>.</param>
/// <param name="AllItems">All of them, the changed files before the new ones and each group by path; cut at <see cref="ResticClient.MaxDiffItems"/>.</param>
public sealed record ResticBackupPlan(
    long FilesNew,
    long FilesChanged,
    long FilesUnmodified,
    long BytesToProcess,
    long BytesToUpload,
    IReadOnlyList<ResticPlanItem> Items,
    bool ItemsTruncated,
    IReadOnlyList<ResticPlanItem> AllItems,
    bool AllItemsTruncated);

public sealed record ResticBackupProgress(double PercentDone, long FilesDone, long TotalFiles, long BytesDone, long TotalBytes);

/// <param name="Incomplete">Some files could not be read; the snapshot was made without them.</param>
public sealed record ResticBackupSummary(
    string SnapshotId,
    long FilesNew,
    long FilesChanged,
    long FilesUnmodified,
    long DataAdded,
    long TotalBytesProcessed,
    bool Incomplete,
    IReadOnlyList<string> Errors);

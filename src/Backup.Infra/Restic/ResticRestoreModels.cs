namespace Backup.Infra.Restic;

public sealed record ResticRestoreProgress(double PercentDone, long FilesRestored, long TotalFiles, long BytesRestored, long TotalBytes);

/// <remarks>restic counts directories among the files.</remarks>
public sealed record ResticRestoreSummary(long FilesRestored, long TotalFiles, long BytesRestored, long TotalBytes);

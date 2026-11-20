namespace Backup.Core;

/// <summary>
/// How many snapshots of a repository are kept. A policy that keeps nothing
/// is not valid, so it can never be used to empty a repository.
/// </summary>
public sealed record RetentionPolicy(int KeepLast = 0, int KeepDaily = 0, int KeepWeekly = 0, int KeepMonthly = 0, int KeepYearly = 0)
{
    private const int Limit = 100_000;

    public RetentionPolicy Validated()
    {
        int[] values = [KeepLast, KeepDaily, KeepWeekly, KeepMonthly, KeepYearly];
        if (values.Any(v => v < 0 || v > Limit))
        {
            throw new DomainException("retention_invalid", $"Each number must be between 0 and {Limit}.");
        }

        if (values.All(v => v == 0))
        {
            throw new DomainException("retention_empty", "Keep at least one snapshot.");
        }

        return this;
    }
}

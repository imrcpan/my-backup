namespace Backup.Infra.Restic;

public enum ResticCheckMode
{
    /// <summary>Checks the structure of the repository only. Fast, reads little.</summary>
    Structure,

    /// <summary>Also reads a share of the data and checks it.</summary>
    Subset,

    /// <summary>Also reads all data. On a cloud drive this downloads the whole repository.</summary>
    Full,
}

/// <param name="Ok">False if restic found errors in the repository.</param>
/// <param name="Output">The last lines restic printed.</param>
public sealed record ResticCheckResult(bool Ok, string Output);

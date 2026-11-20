using Backup.Core;

namespace Backup.Infra.Storage;

public static class SourceGuard
{
    /// <summary>
    /// A directory that is to be backed up must exist and must hold something. An empty one
    /// is what an unmounted disk looks like, and backing it up would record a loss.
    /// </summary>
    /// <exception cref="DomainException">"source_missing" or "source_empty".</exception>
    public static void EnsureUsable(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DomainException("source_missing", $"The directory {directory} does not exist.");
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new DomainException("source_empty", $"The directory {directory} is empty. Is the disk mounted?");
        }
    }
}

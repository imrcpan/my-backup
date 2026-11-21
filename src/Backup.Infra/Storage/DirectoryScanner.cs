using System.IO.Enumeration;

namespace Backup.Infra.Storage;

public sealed record DirectoryScan(long Files, long Bytes, IReadOnlyList<(string Path, long Size)> First, bool Truncated);

public static class DirectoryScanner
{
    /// <summary>
    /// Counts the files below a directory and returns the first few. It is used where restic cannot tell what a
    /// backup would do, because the repository does not exist yet. Unreadable entries are skipped, and links are
    /// not followed: a link that leads back up would otherwise make the walk endless, and what is behind a link
    /// would be counted twice. Only regular files are counted.
    /// </summary>
    /// <exception cref="OperationCanceledException">The scan was cancelled; a big source takes a while.</exception>
    public static DirectoryScan Scan(string directory, int maxLines, CancellationToken cancellationToken)
    {
        FileSystemEnumerable<(string Path, long Length)> files = new(
            directory,
            (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Length),
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && !IsLink(entry),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => entry.IsDirectory && !IsLink(entry),
        };

        long count = 0, bytes = 0;
        List<(string, long)> first = [];
        foreach ((string path, long length) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            bytes += length;
            if (first.Count < maxLines)
            {
                first.Add((path, length));
            }
        }

        return new DirectoryScan(count, bytes, first, count > first.Count);
    }

    private static bool IsLink(FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) != 0;
}

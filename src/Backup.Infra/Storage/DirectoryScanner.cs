namespace Backup.Infra.Storage;

public sealed record DirectoryScan(long Files, long Bytes, IReadOnlyList<(string Path, long Size)> First, bool Truncated);

public static class DirectoryScanner
{
    /// <summary>
    /// Counts the files below a directory and returns the first few. It is used where restic cannot
    /// tell what a backup would do, because the repository does not exist yet. Unreadable entries are skipped.
    /// </summary>
    public static DirectoryScan Scan(string directory, int maxLines)
    {
        EnumerationOptions options = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        long files = 0, bytes = 0;
        List<(string, long)> first = [];
        foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*", options))
        {
            long size;
            try
            {
                size = file.Length;
            }
            catch (IOException)
            {
                continue;
            }

            files++;
            bytes += size;
            if (first.Count < maxLines)
            {
                first.Add((file.FullName, size));
            }
        }

        return new DirectoryScan(files, bytes, first, files > first.Count);
    }
}

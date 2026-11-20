namespace Backup.Infra.Persistence;

public static class AtomicFile
{
    /// <summary>
    /// Writes to a temporary file next to the target and renames it over the target, so a reader
    /// or a crash sees the old content or the new one, never half of it. Only the owner can read it.
    /// </summary>
    public static void WriteAllText(string path, string content)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (FileStream stream = new(temporary, options))
            using (StreamWriter writer = new(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

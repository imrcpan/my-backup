using Backup.Core;

namespace Backup.Infra.Persistence;

/// <summary>
/// Holds a data directory for one running instance. Two instances that write the same state and
/// run history would overwrite each other's changes, and could run the same backup twice.
/// The lock is a file that is kept open and locked; the system releases it when the program ends, even if it crashes.
/// The number of the process that holds it is kept in a file next to it, only to tell the user which process it is.
/// </summary>
public sealed class DataDirectoryLock : IDisposable
{
    private readonly FileStream _file;
    private readonly string _pidPath;

    private DataDirectoryLock(FileStream file, string pidPath)
    {
        _file = file;
        _pidPath = pidPath;
    }

    /// <exception cref="DomainException">"data_dir_in_use" if another instance has the directory.</exception>
    public static DataDirectoryLock Acquire(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ".lock");
        string pidPath = Path.Combine(directory, ".lock.pid");
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            string holder = HolderOf(pidPath) is int pid ? $" (process {pid})" : "";
            throw new DomainException(
                "data_dir_in_use",
                $"Another instance of the application{holder} is using {directory}. Stop it, or use another data directory with --DataDir.");
        }

        try
        {
            File.WriteAllText(pidPath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // The number is only a hint for a message; the lock is what counts.
        }

        return new DataDirectoryLock(file, pidPath);
    }

    /// <summary>The process that wrote the file, if it is still there. A number left by a program that crashed is not believed.</summary>
    private static int? HolderOf(string pidPath)
    {
        try
        {
            return int.TryParse(File.ReadAllText(pidPath).Trim(), out int pid) && Directory.Exists($"/proc/{pid}") ? pid : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_pidPath);
        }
        catch (IOException)
        {
            // Not a reason to fail when the program ends.
        }

        _file.Dispose();
    }
}

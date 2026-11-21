using Backup.Core;

namespace Backup.Infra.Persistence;

/// <summary>
/// Holds a data directory for one running instance. Two instances that write the same state and
/// run history would overwrite each other's changes, and could run the same backup twice.
/// The lock is a file that is kept open and locked; the system releases it when the program ends, even if it crashes.
/// </summary>
public sealed class DataDirectoryLock : IDisposable
{
    private readonly FileStream _file;

    private DataDirectoryLock(FileStream file) => _file = file;

    /// <exception cref="DomainException">"data_dir_in_use" if another instance has the directory.</exception>
    public static DataDirectoryLock Acquire(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ".lock");
        try
        {
            FileStream file = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new DataDirectoryLock(file);
        }
        catch (IOException)
        {
            throw new DomainException("data_dir_in_use", $"Another instance of the application is using {directory}. Stop it, or use another data directory.");
        }
    }

    public void Dispose() => _file.Dispose();
}

namespace Backup.Core;

/// <summary>Everything that is saved between runs of the application.</summary>
public sealed class AppState
{
    public List<StorageDevice> Devices { get; set; } = [];

    public List<BackupGroup> Groups { get; set; } = [];
}

public interface IStateStore
{
    /// <summary>Returns the saved state, or an empty one if nothing was saved yet.</summary>
    AppState Load();

    void Save(AppState state);
}

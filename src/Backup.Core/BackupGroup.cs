namespace Backup.Core;

public sealed class BackupGroup
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<StorageObject> Objects { get; set; } = [];
}

namespace Backup.Core;

/// <summary>
/// The backup groups and their storage objects. Removing a group or an
/// object only forgets the configuration; no data is ever deleted here.
/// </summary>
public sealed class BackupGroupCatalog
{
    private readonly List<BackupGroup> _groups = [];

    public BackupGroupCatalog()
    {
    }

    public BackupGroupCatalog(IEnumerable<BackupGroup> saved)
    {
        _groups.AddRange(saved);
    }

    public IReadOnlyList<BackupGroup> Groups => _groups;

    public BackupGroup? Find(string id) => _groups.Find(g => g.Id == id);

    public BackupGroup Get(string id) =>
        Find(id) ?? throw new DomainException("group_not_found", "The backup group does not exist.");

    /// <summary>Creates an empty group; the name is all the user gives.</summary>
    public BackupGroup AddGroup(string? name)
    {
        string valid = ValidName(name);
        EnsureNameFree(valid, null);
        BackupGroup group = new() { Id = NewId("g"), Name = valid };
        _groups.Add(group);
        return group;
    }

    public BackupGroup RenameGroup(string id, string? name)
    {
        BackupGroup group = Get(id);
        string valid = ValidName(name);
        EnsureNameFree(valid, group);
        group.Name = valid;
        return group;
    }

    public void RemoveGroup(string id) => _groups.Remove(Get(id));

    public StorageObject AddObject(string groupId, string? deviceId, string? path, bool encrypted, string? password)
    {
        BackupGroup group = Get(groupId);
        StorageObject item = Build(NewId("o"), deviceId, path, encrypted, password, null);
        EnsureLocationFree(group, item);
        group.Objects.Add(item);
        return item;
    }

    /// <summary>
    /// An empty password keeps the saved one, but only if the object was
    /// encrypted already; turning encryption on always needs a password.
    /// </summary>
    public StorageObject UpdateObject(string groupId, string objectId, string? deviceId, string? path, bool encrypted, string? password)
    {
        BackupGroup group = Get(groupId);
        int index = group.Objects.FindIndex(o => o.Id == objectId);
        if (index < 0)
        {
            throw new DomainException("object_not_found", "The storage object does not exist.");
        }

        StorageObject old = group.Objects[index];
        StorageObject item = Build(old.Id, deviceId, path, encrypted, password, old.Password) with { Retention = encrypted ? old.Retention : null };
        EnsureLocationFree(group, item);
        group.Objects[index] = item;
        return item;
    }

    /// <summary>Sets how many snapshots of an encrypted object are kept; null removes the policy.</summary>
    public StorageObject SetRetention(string groupId, string objectId, RetentionPolicy? policy)
    {
        BackupGroup group = Get(groupId);
        int index = group.Objects.FindIndex(o => o.Id == objectId);
        if (index < 0)
        {
            throw new DomainException("object_not_found", "The storage object does not exist.");
        }

        if (policy != null && !group.Objects[index].Encrypted)
        {
            throw new DomainException("retention_needs_repository", "Only an encrypted object holds snapshots.");
        }

        StorageObject item = group.Objects[index] with { Retention = policy?.Validated() };
        group.Objects[index] = item;
        return item;
    }

    public void RemoveObject(string groupId, string objectId)
    {
        BackupGroup group = Get(groupId);
        if (group.Objects.RemoveAll(o => o.Id == objectId) == 0)
        {
            throw new DomainException("object_not_found", "The storage object does not exist.");
        }
    }

    private static StorageObject Build(string id, string? deviceId, string? path, bool encrypted, string? password, string? savedPassword)
    {
        string device = (deviceId ?? "").Trim();
        if (device.Length == 0)
        {
            throw new DomainException("object_device_required", "Choose a storage device.");
        }

        string? keep = null;
        if (encrypted)
        {
            keep = string.IsNullOrEmpty(password) ? savedPassword : password;
            if (string.IsNullOrEmpty(keep))
            {
                throw new DomainException("object_password_required", "An encrypted object needs a password.");
            }
        }

        return new StorageObject(id, device, StoragePath.Normalize(path), encrypted, keep);
    }

    private static void EnsureLocationFree(BackupGroup group, StorageObject item)
    {
        if (group.Objects.Any(o => o.Id != item.Id && o.DeviceId == item.DeviceId && o.Path == item.Path))
        {
            throw new DomainException("object_exists", "This device and path are already in the group.");
        }
    }

    private static string ValidName(string? name)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 64)
        {
            throw new DomainException("group_name_invalid", "The name must be 1-64 characters.");
        }

        return trimmed;
    }

    private void EnsureNameFree(string name, BackupGroup? except)
    {
        if (_groups.Any(g => g != except && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("group_name_taken", "Another group already uses this name.");
        }
    }

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12];
}

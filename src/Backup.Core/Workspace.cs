namespace Backup.Core;

/// <summary>
/// The storage devices and the backup groups, kept together so that the rules
/// between them hold, and saved after every change. All access is under one lock.
/// </summary>
public sealed class Workspace
{
    private readonly object _gate = new();
    private readonly IStateStore _store;
    private readonly StorageDeviceCatalog _devices;
    private readonly BackupGroupCatalog _groups;

    public Workspace(IStateStore store)
    {
        _store = store;
        AppState state = store.Load();
        _devices = new StorageDeviceCatalog(state.Devices);
        _groups = new BackupGroupCatalog(state.Groups);
    }

    /// <summary>Reads under the lock. The catalogs must not be kept or changed by the caller.</summary>
    public T Read<T>(Func<StorageDeviceCatalog, BackupGroupCatalog, T> read)
    {
        lock (_gate)
        {
            return read(_devices, _groups);
        }
    }

    /// <summary>Changes under the lock and saves. If the change throws, nothing is saved; a change must therefore check before it mutates.</summary>
    public T Write<T>(Func<StorageDeviceCatalog, BackupGroupCatalog, T> change)
    {
        lock (_gate)
        {
            T result = change(_devices, _groups);
            _store.Save(new AppState { Devices = [.. _devices.Devices], Groups = [.. _groups.Groups] });
            return result;
        }
    }

    /// <summary>A device that a storage object uses cannot be removed; it would leave the object without a place.</summary>
    public void RemoveDevice(string deviceId) => Write((devices, groups) =>
    {
        if (groups.Groups.Any(g => g.Objects.Any(o => o.DeviceId == deviceId)))
        {
            throw new DomainException("device_in_use", "A storage object still uses this device.");
        }

        devices.Remove(deviceId);
        return 0;
    });

    /// <summary>A cloud drive may not become the home of a plain object, so the kind of a device in use cannot turn into one.</summary>
    public StorageDevice UpdateDevice(string deviceId, StorageDevice device) => Write((devices, groups) =>
    {
        // Checked before the catalog is changed, so a refusal leaves everything as it was.
        if (device.Kind == StorageDeviceKind.Rclone && groups.Groups.Any(g => g.Objects.Any(o => o.DeviceId == deviceId && !o.Encrypted)))
        {
            throw new DomainException("cloud_requires_encryption", "A plain object still uses this device, and data on a cloud drive must be encrypted.");
        }

        return devices.Update(deviceId, device);
    });

    /// <summary>The device must exist; only its id is used from then on. Data on a cloud drive must be encrypted.</summary>
    public StorageObject AddObject(string groupId, string? deviceId, string? path, bool encrypted, string? password) => Write((devices, groups) =>
    {
        EnsureDevice(devices, deviceId, encrypted);
        return groups.AddObject(groupId, deviceId, path, encrypted, password);
    });

    public StorageObject UpdateObject(string groupId, string objectId, string? deviceId, string? path, bool encrypted, string? password) => Write((devices, groups) =>
    {
        EnsureDevice(devices, deviceId, encrypted);
        return groups.UpdateObject(groupId, objectId, deviceId, path, encrypted, password);
    });

    private static void EnsureDevice(StorageDeviceCatalog devices, string? deviceId, bool encrypted)
    {
        StorageDevice device = devices.Find((deviceId ?? "").Trim())
            ?? throw new DomainException("device_not_found", "The storage device does not exist.");
        if (device.Kind == StorageDeviceKind.Rclone && !encrypted)
        {
            throw new DomainException("cloud_requires_encryption", "Data on a cloud drive must be encrypted.");
        }
    }
}

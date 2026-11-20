namespace Backup.Core;

/// <summary>
/// All known storage devices. The local one and Google Drive are always
/// present, in this order, and cannot be removed.
/// </summary>
public sealed class StorageDeviceCatalog
{
    private readonly List<StorageDevice> _devices = [StorageDevice.Local, StorageDevice.GoogleDrive];

    public StorageDeviceCatalog()
    {
    }

    public StorageDeviceCatalog(IEnumerable<StorageDevice> saved)
    {
        foreach (StorageDevice device in saved)
        {
            int index = _devices.FindIndex(d => d.Id == device.Id);
            if (index >= 0)
            {
                // Only the settings of a built-in device may differ from the default.
                _devices[index] = device.Validated();
            }
            else
            {
                Add(device);
            }
        }
    }

    public IReadOnlyList<StorageDevice> Devices => _devices;

    public StorageDevice? Find(string id) => _devices.Find(d => d.Id == id);

    public StorageDevice Add(StorageDevice device)
    {
        StorageDevice valid = device.Validated();
        EnsureUnique(valid, null);
        _devices.Add(valid);
        return valid;
    }

    public StorageDevice Update(string id, StorageDevice device)
    {
        int index = _devices.FindIndex(d => d.Id == id);
        if (index < 0)
        {
            throw new DomainException("device_not_found", "The storage device does not exist.");
        }

        StorageDevice valid = device.Validated();
        if (_devices[index].IsBuiltIn && (valid.Id != id || valid.Kind != _devices[index].Kind))
        {
            throw new DomainException("device_builtin", "The id and kind of a built-in device cannot change.");
        }

        EnsureUnique(valid, index);
        _devices[index] = valid;
        return valid;
    }

    public void Remove(string id)
    {
        StorageDevice device = Find(id) ?? throw new DomainException("device_not_found", "The storage device does not exist.");
        if (device.IsBuiltIn)
        {
            throw new DomainException("device_builtin", "A built-in device cannot be removed.");
        }

        _devices.Remove(device);
    }

    private void EnsureUnique(StorageDevice device, int? ignoreIndex)
    {
        for (int i = 0; i < _devices.Count; i++)
        {
            if (i == ignoreIndex)
            {
                continue;
            }

            if (_devices[i].Id == device.Id)
            {
                throw new DomainException("device_id_taken", "Another device already uses this id.");
            }

            if (string.Equals(_devices[i].Name, device.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainException("device_name_taken", "Another device already uses this name.");
            }
        }
    }
}

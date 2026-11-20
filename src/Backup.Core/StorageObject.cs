namespace Backup.Core;

/// <summary>
/// A path on a storage device that belongs to a backup group. Encryption
/// is decided per object: the unit is device plus path, and nothing
/// outside this path is touched.
/// </summary>
/// <param name="Id">Stable key, unique in the group.</param>
/// <param name="DeviceId">Only the id of the device is kept, never its details.</param>
/// <param name="Path">Normalized by <see cref="StoragePath"/>.</param>
/// <param name="Password">Set if and only if <paramref name="Encrypted"/> is true.</param>
public sealed record StorageObject(string Id, string DeviceId, string Path, bool Encrypted, string? Password);

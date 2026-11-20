using System.Text.RegularExpressions;

namespace Backup.Core;

public enum StorageDeviceKind
{
    /// <summary>A directory tree of this computer.</summary>
    Local,

    /// <summary>A disk that is checked by its file system UUID before use.</summary>
    ExternalDisk,

    /// <summary>A cloud drive reached through an rclone remote.</summary>
    Rclone,
}

/// <summary>
/// Where data can live. It holds facts about the device only; whether
/// and how a path on it is encrypted belongs to a storage object.
/// </summary>
/// <param name="Id">Stable key used by backup groups. Unique.</param>
/// <param name="Name">Label shown to the user. Unique.</param>
/// <param name="Uuid">File system UUID, for an external disk.</param>
/// <param name="RcloneRemote">Name of the rclone remote, for a cloud drive.</param>
public sealed partial record StorageDevice(
    string Id,
    string Name,
    StorageDeviceKind Kind,
    string? Uuid = null,
    string? RcloneRemote = null)
{
    public const string LocalId = "local";
    public const string GoogleDriveId = "gdrive";

    public bool IsBuiltIn => Id == LocalId || Id == GoogleDriveId;

    public static StorageDevice Local { get; } = new(LocalId, "Local", StorageDeviceKind.Local);

    public static StorageDevice GoogleDrive { get; } =
        new(GoogleDriveId, "Google Drive", StorageDeviceKind.Rclone, RcloneRemote: "gdrive");

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,63}$")]
    private static partial Regex UuidPattern();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex RemotePattern();

    /// <summary>Returns a normalized copy, or throws <see cref="DomainException"/>.</summary>
    public StorageDevice Validated()
    {
        string id = Id.Trim();
        string name = Name.Trim();
        if (!IdPattern().IsMatch(id))
        {
            throw new DomainException("device_id_invalid", "The id must be 1-32 characters of a-z, 0-9, '_' or '-'.");
        }

        if (name.Length == 0 || name.Length > 64)
        {
            throw new DomainException("device_name_invalid", "The name must be 1-64 characters.");
        }

        string? uuid = Uuid?.Trim();
        string? remote = RcloneRemote?.Trim().TrimEnd(':');
        switch (Kind)
        {
            case StorageDeviceKind.ExternalDisk when uuid == null || !UuidPattern().IsMatch(uuid):
                throw new DomainException("device_uuid_invalid", "An external disk needs a file system UUID.");
            case StorageDeviceKind.Rclone when remote == null || !RemotePattern().IsMatch(remote):
                throw new DomainException("device_remote_invalid", "A cloud drive needs an rclone remote name.");
        }

        return this with
        {
            Id = id,
            Name = name,
            Uuid = Kind == StorageDeviceKind.ExternalDisk ? uuid : null,
            RcloneRemote = Kind == StorageDeviceKind.Rclone ? remote : null,
        };
    }
}

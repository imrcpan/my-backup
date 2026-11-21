using Backup.Core;
using Backup.Infra.Storage;

namespace Backup.App.Browsing;

/// <param name="Type">"dir", "file" or "symlink".</param>
/// <param name="Path">Inside the storage object, starting with "/".</param>
public sealed record FileEntry(string Name, string Type, string Path, long? Size, DateTimeOffset? Modified);

public sealed record FileListing(IReadOnlyList<FileEntry> Entries, bool Truncated);

/// <summary>Lists the files of a plain storage object. It only reads, and it never leaves the object.</summary>
public sealed class ObjectBrowser(Workspace workspace, IDeviceResolver resolver)
{
    public const int MaxEntries = 2000;

    /// <exception cref="DomainException">"object_is_a_repository", "path_not_found", "path_invalid", "path_escapes_object", or what the device says.</exception>
    public FileListing List(string groupId, string objectId, string? path)
    {
        (StorageObject item, StorageDevice device) = workspace.Read((devices, groups) =>
        {
            StorageObject item = groups.Get(groupId).Objects.Find(o => o.Id == objectId)
                ?? throw new DomainException("object_not_found", "The storage object does not exist.");
            return (item, devices.Find(item.DeviceId) ?? throw new DomainException("device_not_found", "The storage device does not exist."));
        });
        if (item.Encrypted)
        {
            throw new DomainException("object_is_a_repository", "An encrypted object holds snapshots; browse those.");
        }

        string root = resolver.Resolve(item, device, forWrite: false).FileSystemPath
            ?? throw new DomainException("object_not_a_folder", "This object is not a folder.");
        string inside = StoragePath.Normalize(path ?? "/");
        string directory = inside == "/" ? root : root.TrimEnd('/') + inside;
        EnsureNoLinks(root, inside);
        if (!Directory.Exists(directory))
        {
            throw new DomainException("path_not_found", "The directory does not exist.");
        }

        List<FileEntry> entries = [];
        bool truncated = false;
        EnumerationOptions options = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options)
                     .OrderBy(e => e is DirectoryInfo ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count == MaxEntries)
            {
                truncated = true;
                break;
            }

            string type = entry.LinkTarget != null ? "symlink" : entry is DirectoryInfo ? "dir" : "file";
            long? size = type == "file" ? ((FileInfo)entry).Length : null;
            entries.Add(new FileEntry(entry.Name, type, inside == "/" ? "/" + entry.Name : inside + "/" + entry.Name, size, entry.LastWriteTimeUtc));
        }

        return new FileListing(entries, truncated);
    }

    /// <summary>A link on the way could lead out of the object, so none is followed.</summary>
    private static void EnsureNoLinks(string root, string inside)
    {
        string current = root;
        foreach (string part in inside.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget != null)
            {
                throw new DomainException("path_escapes_object", "The path goes through a link.");
            }
        }
    }
}

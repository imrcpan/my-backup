using System.Text.Json;
using Backup.Infra.Process;

namespace Backup.Infra.Disks;

/// <param name="Uuid">The UUID of the file system, which is what a storage device is identified by.</param>
/// <param name="Device">The device node, such as /dev/sda1.</param>
/// <param name="Removable">An external disk: removable or hot-pluggable, for example on USB.</param>
public sealed record DiskInfo(
    string Uuid,
    string Device,
    string? Label,
    string? FileSystem,
    long? SizeBytes,
    string? Model,
    string? Transport,
    bool Removable,
    string? MountPoint);

public interface IDiskLister
{
    /// <summary>The file systems that are attached now, external disks first.</summary>
    Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Lists the attached file systems with lsblk. If lsblk is not there, it falls back to the
/// links in /dev/disk/by-uuid, which tell much less (no label, size or model).
/// </summary>
public sealed class DiskLister(
    IProcessRunner runner,
    string lsblkPath = "lsblk",
    string byUuidDirectory = "/dev/disk/by-uuid",
    string mountInfoPath = "/proc/self/mountinfo") : IDiskLister
{
    /// <summary>Things that have a UUID but are not file systems that hold files.</summary>
    private static readonly HashSet<string> NotFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "swap", "crypto_LUKS", "LVM2_member", "linux_raid_member", "zfs_member", "bcache", "isw_raid_member",
    };

    public async Task<IReadOnlyList<DiskInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        foreach (string columns in new[]
        {
            "NAME,PATH,UUID,LABEL,SIZE,FSTYPE,MOUNTPOINTS,RM,HOTPLUG,TRAN,MODEL",
            "NAME,PATH,UUID,LABEL,SIZE,FSTYPE,MOUNTPOINT,RM,HOTPLUG,TRAN,MODEL",
        })
        {
            try
            {
                ProcessResult result = await runner.RunAsync(
                    new ProcessSpec(lsblkPath, ["--json", "--bytes", "--output", columns]), cancellationToken: cancellationToken);
                if (result.Succeeded)
                {
                    return Sort(Parse(result.StandardOutput));
                }
            }
            catch (ExternalToolException)
            {
                break;
            }
        }

        return Sort(FromByUuid());
    }

    private static IReadOnlyList<DiskInfo> Sort(IEnumerable<DiskInfo> disks) =>
        [.. disks.OrderByDescending(d => d.Removable).ThenBy(d => d.Device, StringComparer.Ordinal)];

    /// <summary>Reads the JSON of lsblk. A partition takes the model, the transport and the removable flag of its disk.</summary>
    internal static IReadOnlyList<DiskInfo> Parse(string json)
    {
        List<DiskInfo> disks = [];
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("blockdevices", out JsonElement roots))
        {
            foreach (JsonElement root in roots.EnumerateArray())
            {
                Walk(root, null, null, false, disks);
            }
        }

        return disks;
    }

    private static void Walk(JsonElement node, string? parentModel, string? parentTransport, bool parentRemovable, List<DiskInfo> disks)
    {
        string? model = Text(node, "model")?.Trim() ?? parentModel;
        string? transport = Text(node, "tran") ?? parentTransport;
        bool removable = parentRemovable || Flag(node, "rm") || Flag(node, "hotplug");

        string? uuid = Text(node, "uuid");
        string? fileSystem = Text(node, "fstype");
        if (uuid != null && fileSystem != null && !NotFileSystems.Contains(fileSystem))
        {
            disks.Add(new DiskInfo(
                uuid, Text(node, "path") ?? "/dev/" + Text(node, "name"), Text(node, "label"), fileSystem,
                Number(node, "size"), model, transport, removable, MountPoint(node)));
        }

        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                Walk(child, model, transport, removable, disks);
            }
        }
    }

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>Older lsblk writes flags as "0" and "1".</summary>
    private static bool Flag(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value)
        && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && value.GetString() == "1"));

    private static long? Number(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64(),
            JsonValueKind.String when long.TryParse(value.GetString(), out long parsed) => parsed,
            _ => null,
        };
    }

    private static string? MountPoint(JsonElement node)
    {
        if (node.TryGetProperty("mountpoints", out JsonElement points) && points.ValueKind == JsonValueKind.Array)
        {
            return points.EnumerateArray().Select(p => p.GetString()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        }

        return Text(node, "mountpoint");
    }

    private IEnumerable<DiskInfo> FromByUuid()
    {
        if (!Directory.Exists(byUuidDirectory))
        {
            yield break;
        }

        DiskProbe probe = new(byUuidDirectory, mountInfoPath);
        foreach (string link in Directory.EnumerateFileSystemEntries(byUuidDirectory))
        {
            string uuid = Path.GetFileName(link);
            DiskStatus status = probe.Check(uuid);
            yield return new DiskInfo(uuid, status.DevicePath ?? link, null, null, null, null, null, false, status.MountPoint);
        }
    }
}

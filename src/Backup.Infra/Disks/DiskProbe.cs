using System.Text;

namespace Backup.Infra.Disks;

public interface IDiskProbe
{
    DiskStatus Check(string uuid);
}

/// <summary>
/// Finds a disk by the UUID of its file system, not by its device name, which
/// changes between connections, and tells whether it is mounted and where.
/// </summary>
public sealed class DiskProbe(
    string byUuidDirectory = "/dev/disk/by-uuid",
    string mountInfoPath = "/proc/self/mountinfo") : IDiskProbe
{
    public DiskStatus Check(string uuid)
    {
        string? device = FindDevice(uuid);
        if (device == null)
        {
            return new DiskStatus(DiskState.NotConnected);
        }

        MountEntry? mount = ParseMountInfo(File.ReadAllLines(mountInfoPath))
            .Where(m => SameFile(m.Source, device))
            .OrderBy(m => m.Root == "/" ? 0 : 1)
            .FirstOrDefault();
        return mount == null
            ? new DiskStatus(DiskState.NotMounted, device)
            : new DiskStatus(DiskState.Mounted, device, mount.MountPoint, mount.ReadOnly);
    }

    private string? FindDevice(string uuid)
    {
        if (!Directory.Exists(byUuidDirectory))
        {
            return null;
        }

        foreach (string link in Directory.EnumerateFileSystemEntries(byUuidDirectory))
        {
            if (!string.Equals(Path.GetFileName(link), uuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileSystemInfo? target = new FileInfo(link).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? Path.GetFullPath(link);
        }

        return null;
    }

    private static bool SameFile(string a, string b)
    {
        return Resolve(a) == Resolve(b);

        static string Resolve(string path)
        {
            try
            {
                return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
            }
            catch (IOException)
            {
                return path;
            }
        }
    }

    internal sealed record MountEntry(string Root, string MountPoint, string Source, bool ReadOnly);

    /// <summary>Reads lines of /proc/self/mountinfo (see proc(5)).</summary>
    internal static IEnumerable<MountEntry> ParseMountInfo(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string[] halves = line.Split(" - ", 2);
            if (halves.Length != 2)
            {
                continue;
            }

            string[] left = halves[0].Split(' ');
            string[] right = halves[1].Split(' ');
            if (left.Length < 6 || right.Length < 2)
            {
                continue;
            }

            bool readOnly = left[5].Split(',').Contains("ro");
            yield return new MountEntry(Unescape(left[3]), Unescape(left[4]), Unescape(right[1]), readOnly);
        }
    }

    /// <summary>The kernel writes space, tab, newline and backslash as \040 \011 \012 \134.</summary>
    internal static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        StringBuilder result = new();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && IsOctal(value, i + 1))
            {
                result.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    private static bool IsOctal(string value, int start) =>
        start + 3 <= value.Length && value.AsSpan(start, 3).IndexOfAnyExceptInRange('0', '7') < 0;
}

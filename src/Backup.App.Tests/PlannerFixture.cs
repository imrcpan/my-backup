using Backup.App.Planning;
using Backup.Core;
using Backup.Infra.Disks;
using Backup.Infra.Process;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;
using Xunit;

namespace Backup.App.Tests;

public sealed class FakeProbe : IDiskProbe
{
    public DiskStatus Status { get; set; } = new(DiskState.NotConnected);

    public DiskStatus Check(string uuid) => Status;
}

public sealed class MemoryStore : IStateStore
{
    public AppState State { get; private set; } = new();

    public AppState Load() => State;

    public void Save(AppState state) => State = state;
}

/// <summary>
/// A scratch world with the real restic and rclone: "local" is this computer, "disk" is an external disk that
/// is mounted in the scratch directory, and "gdrive" is a cloud drive that rclone serves from the scratch directory.
/// </summary>
public sealed class PlannerFixture : IDisposable
{
    public PlannerFixture()
    {
        Root = Directory.CreateTempSubdirectory("planner").FullName;
        Mount = Directory.CreateDirectory(Path.Combine(Root, "mount")).FullName;
        Cloud = Directory.CreateDirectory(Path.Combine(Root, "cloud")).FullName;
        Home = Directory.CreateDirectory(Path.Combine(Root, "home")).FullName;
        RcloneConfig = Path.Combine(Root, "rclone.conf");
        File.WriteAllText(RcloneConfig, "[gdrive]\ntype = local\n");
        Probe = new FakeProbe { Status = new DiskStatus(DiskState.Mounted, "/dev/fake1", Mount) };

        ProcessRunner runner = new();
        Restic = new ResticClient(runner, new ResticOptions(RcloneConfigFile: RcloneConfig));
        Rclone = new RcloneClient(runner, new RcloneOptions(ConfigFile: RcloneConfig));
        Workspace = new Workspace(new MemoryStore());
        Workspace.Write((devices, _) => devices.Add(new StorageDevice("disk", "Media disk", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD")));
        GroupId = Workspace.Write((_, groups) => groups.AddGroup("Media")).Id;
        Planner = new RunPlanner(Workspace, new DeviceResolver(Probe), Restic, Rclone, TimeProvider.System);
    }

    public string Root { get; }

    /// <summary>Where the external disk is mounted.</summary>
    public string Mount { get; }

    /// <summary>What the cloud drive "gdrive" shows as its root.</summary>
    public string Cloud { get; }

    public string Home { get; }

    public string RcloneConfig { get; }

    public FakeProbe Probe { get; }

    public ResticClient Restic { get; }

    public RcloneClient Rclone { get; }

    public Workspace Workspace { get; }

    public string GroupId { get; }

    public RunPlanner Planner { get; }

    public CancellationToken Ct => TestContext.Current.CancellationToken;

    public StorageObject Add(string device, string path, bool encrypted) =>
        Workspace.AddObject(GroupId, device, path, encrypted, encrypted ? "pw-" + path.Replace('/', '_') : null);

    /// <summary>A plain object on this computer, with files in it.</summary>
    public StorageObject LocalSource(string name = "docs")
    {
        string dir = Directory.CreateDirectory(Path.Combine(Home, name)).FullName;
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub/b.txt"), "world!");
        return Add("local", dir, false);
    }

    public ResticRepository RepoOf(StorageObject item) =>
        new(item.DeviceId == "gdrive" ? $"rclone:gdrive:{item.Path}" : item.Path, item.Password!);

    public void Dispose() => Directory.Delete(Root, true);
}

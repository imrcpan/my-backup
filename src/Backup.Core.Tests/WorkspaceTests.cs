using Xunit;

namespace Backup.Core.Tests;

public class WorkspaceTests
{
    private sealed class MemoryStore : IStateStore
    {
        public AppState State { get; set; } = new();

        public int Saves { get; private set; }

        public AppState Load() => State;

        public void Save(AppState state)
        {
            Saves++;
            State = state;
        }
    }

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void Every_change_is_saved_and_survives_a_restart()
    {
        MemoryStore store = new();
        Workspace first = new(store);
        string group = first.Write((_, groups) => groups.AddGroup("Photos")).Id;
        first.Write((devices, _) => devices.Add(new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD")));
        first.AddObject(group, "media", "/photos/", true, "secret");
        Assert.Equal(3, store.Saves);

        Workspace second = new(store);
        StorageObject saved = second.Read((_, groups) => groups.Get(group).Objects.Single());
        Assert.Equal("/photos", saved.Path);
        Assert.Equal("secret", saved.Password);
        Assert.Equal(["local", "gdrive", "media"], second.Read((devices, _) => devices.Devices.Select(d => d.Id).ToArray()));
    }

    [Fact]
    public void A_failed_change_is_not_saved()
    {
        MemoryStore store = new();
        Workspace workspace = new(store);
        Assert.Equal("group_name_invalid", CodeOf(() => workspace.Write((_, groups) => groups.AddGroup(" "))));
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void An_object_needs_an_existing_device()
    {
        Workspace workspace = new(new MemoryStore());
        string group = workspace.Write((_, groups) => groups.AddGroup("G")).Id;
        Assert.Equal("device_not_found", CodeOf(() => workspace.AddObject(group, "nope", "/x", false, null)));
        Assert.Equal("device_not_found", CodeOf(() => workspace.AddObject(group, null, "/x", false, null)));
        workspace.AddObject(group, " local ", "/x", false, null);
    }

    [Fact]
    public void A_device_in_use_cannot_be_removed_until_the_object_is_gone()
    {
        Workspace workspace = new(new MemoryStore());
        string group = workspace.Write((_, groups) => groups.AddGroup("G")).Id;
        workspace.Write((devices, _) => devices.Add(new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD")));
        StorageObject item = workspace.AddObject(group, "media", "/x", false, null);

        Assert.Equal("device_in_use", CodeOf(() => workspace.RemoveDevice("media")));

        workspace.Write((_, groups) => { groups.RemoveObject(group, item.Id); return 0; });
        workspace.RemoveDevice("media");
        Assert.Null(workspace.Read((devices, _) => devices.Find("media")));
    }

    [Fact]
    public void Replacing_a_disk_under_the_same_id_keeps_the_objects_working()
    {
        Workspace workspace = new(new MemoryStore());
        string group = workspace.Write((_, groups) => groups.AddGroup("G")).Id;
        workspace.Write((devices, _) => devices.Add(new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "AAAA-AAAA")));
        StorageObject item = workspace.AddObject(group, "media", "/x", false, null);

        workspace.Write((devices, _) => devices.Update("media", new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "BBBB-BBBB")));

        Assert.Equal("media", workspace.Read((_, groups) => groups.Get(group).Objects.Single(o => o.Id == item.Id).DeviceId));
    }

    [Fact]
    public void Data_on_a_cloud_drive_must_be_encrypted()
    {
        Workspace workspace = new(new MemoryStore());
        string group = workspace.Write((_, groups) => groups.AddGroup("G")).Id;
        Assert.Equal("cloud_requires_encryption", CodeOf(() => workspace.AddObject(group, "gdrive", "/x", false, null)));
        Assert.Empty(workspace.Read((_, groups) => groups.Get(group).Objects));

        StorageObject item = workspace.AddObject(group, "gdrive", "/x", true, "pw");
        Assert.Equal("cloud_requires_encryption", CodeOf(() => workspace.UpdateObject(group, item.Id, "gdrive", "/x", false, null)));
        Assert.True(workspace.Read((_, groups) => groups.Get(group).Objects.Single().Encrypted));

        // A plain object on a local device may not be moved to the cloud either.
        StorageObject plain = workspace.AddObject(group, "local", "/data", false, null);
        Assert.Equal("cloud_requires_encryption", CodeOf(() => workspace.UpdateObject(group, plain.Id, "gdrive", "/data", false, null)));
    }

    [Fact]
    public void A_device_in_use_by_a_plain_object_cannot_become_a_cloud_drive()
    {
        Workspace workspace = new(new MemoryStore());
        string group = workspace.Write((_, groups) => groups.AddGroup("G")).Id;
        workspace.Write((devices, _) => devices.Add(new("disk", "Disk", StorageDeviceKind.ExternalDisk, Uuid: "AAAA-AAAA")));
        workspace.AddObject(group, "disk", "/x", false, null);

        StorageDevice asCloud = new("disk", "Disk", StorageDeviceKind.Rclone, RcloneRemote: "r");
        Assert.Equal("cloud_requires_encryption", CodeOf(() => workspace.UpdateDevice("disk", asCloud)));
        Assert.Equal(StorageDeviceKind.ExternalDisk, workspace.Read((devices, _) => devices.Find("disk")!.Kind));
    }

    [Fact]
    public async Task Concurrent_changes_do_not_lose_any()
    {
        MemoryStore store = new();
        Workspace workspace = new(store);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => workspace.Write((_, groups) => groups.AddGroup("g" + i)))));
        Assert.Equal(50, workspace.Read((_, groups) => groups.Groups.Count));
        Assert.Equal(50, store.State.Groups.Count);
    }

    private sealed class FailingStore : IStateStore
    {
        public AppState Saved { get; private set; } = new();

        public bool Fail { get; set; }

        public AppState Load() => Saved;

        public void Save(AppState state)
        {
            if (Fail)
            {
                throw new IOException("No space left on device");
            }

            Saved = state;
        }
    }

    [Fact]
    public void A_change_that_cannot_be_saved_is_undone_so_that_memory_and_disk_agree()
    {
        FailingStore store = new();
        Workspace workspace = new(store);
        string kept = workspace.Write((_, groups) => groups.AddGroup("Kept")).Id;

        store.Fail = true;
        Assert.Throws<IOException>(() => workspace.Write((_, groups) => groups.AddGroup("Lost")));
        Assert.Throws<IOException>(() => workspace.AddObject(kept, "local", "/x", false, null));
        Assert.Throws<IOException>(() => workspace.Write((devices, _) => devices.Add(new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD"))));

        Assert.Equal(["Kept"], workspace.Read((_, groups) => groups.Groups.Select(g => g.Name).ToArray()));
        Assert.Empty(workspace.Read((_, groups) => groups.Get(kept).Objects));
        Assert.Null(workspace.Read((devices, _) => devices.Find("media")));

        // The next change that can be saved does not carry the ones that could not.
        store.Fail = false;
        workspace.Write((_, groups) => groups.AddGroup("Later"));
        Assert.Equal(["Kept", "Later"], store.Saved.Groups.Select(g => g.Name));
        Assert.DoesNotContain(store.Saved.Devices, d => d.Id == "media");
    }
}

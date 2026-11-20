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
    public async Task Concurrent_changes_do_not_lose_any()
    {
        MemoryStore store = new();
        Workspace workspace = new(store);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => workspace.Write((_, groups) => groups.AddGroup("g" + i)))));
        Assert.Equal(50, workspace.Read((_, groups) => groups.Groups.Count));
        Assert.Equal(50, store.State.Groups.Count);
    }
}

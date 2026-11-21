using Backup.Core;
using Backup.Infra.Persistence;
using Xunit;

namespace Backup.Infra.Tests;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("state-test").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string StatePath => Path.Combine(_dir, "data", "state.json");

    [Fact]
    public void Nothing_saved_gives_an_empty_state()
    {
        AppState state = new JsonStateStore(StatePath).Load();
        Assert.Empty(state.Devices);
        Assert.Empty(state.Groups);
    }

    [Fact]
    public void State_round_trips_with_devices_groups_objects_and_passwords()
    {
        AppState state = new();
        state.Devices.Add(new("media", "Media", StorageDeviceKind.ExternalDisk, Uuid: "1234-ABCD"));
        state.Devices.Add(StorageDevice.GoogleDrive);
        BackupGroup group = new() { Id = "g1", Name = "Photos" };
        group.Objects.Add(new("o1", "gdrive", "/repo", true, "p@ss \"quoted\" ünï"));
        group.Objects.Add(new("o2", "media", "/photos", false, null));
        state.Groups.Add(group);
        JsonStateStore store = new(StatePath);

        store.Save(state);
        AppState loaded = new JsonStateStore(StatePath).Load();

        Assert.Equal(state.Devices, loaded.Devices);
        BackupGroup back = Assert.Single(loaded.Groups);
        Assert.Equal("Photos", back.Name);
        Assert.Equal(group.Objects, back.Objects);
        Assert.Contains("\"ExternalDisk\"", File.ReadAllText(StatePath));
    }

    [Fact]
    public void Save_leaves_no_temporary_file_and_replaces_the_old_content()
    {
        JsonStateStore store = new(StatePath);
        store.Save(new AppState { Groups = [new BackupGroup { Id = "a", Name = "First" }] });
        store.Save(new AppState { Groups = [new BackupGroup { Id = "b", Name = "Second" }] });
        Assert.Equal(["state.json"], Directory.GetFiles(Path.GetDirectoryName(StatePath)!).Select(Path.GetFileName));
        Assert.Equal("Second", store.Load().Groups.Single().Name);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void State_file_is_readable_by_the_owner_only()
    {
        new JsonStateStore(StatePath).Save(new AppState());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StatePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("")]
    public void A_corrupt_file_is_reported_and_left_untouched(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, content);
        DomainException error = Assert.Throws<DomainException>(() => new JsonStateStore(StatePath).Load());
        Assert.Equal("state_corrupt", error.Code);
        Assert.Equal(content, File.ReadAllText(StatePath));
    }

    [Fact]
    public void Workspace_works_on_top_of_the_file()
    {
        Workspace first = new(new JsonStateStore(StatePath));
        string group = first.Write((_, groups) => groups.AddGroup("Docs")).Id;
        first.AddObject(group, "gdrive", "/backups/docs/", true, "pw");

        Workspace second = new(new JsonStateStore(StatePath));
        StorageObject item = second.Read((_, groups) => groups.Get(group).Objects.Single());
        Assert.Equal(("/backups/docs", "pw"), (item.Path, item.Password));
    }

    [Fact]
    public void A_file_written_by_another_version_with_fields_that_are_not_known_or_missing_is_still_read()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, """
            {
              "FutureSetting": {"a": [1,2,3]},
              "Devices": [{"Id":"media","Name":"Media","Kind":"ExternalDisk","Uuid":"1234-ABCD","Colour":"red"}],
              "Groups": [{"Id":"g1","Name":"G","Note":"hello","Objects":[{"Id":"o1","DeviceId":"media","Path":"/x","Encrypted":false}]}]
            }
            """);

        AppState state = new JsonStateStore(StatePath).Load();

        Assert.Equal("media", Assert.Single(state.Devices).Id);
        StorageObject item = Assert.Single(Assert.Single(state.Groups).Objects);
        Assert.Equal("/x", item.Path);
        Assert.Null(item.Password);
        Assert.Null(item.Retention);
    }

    [Fact]
    public void A_value_of_an_enum_that_is_not_known_is_reported_as_a_corrupt_file_and_the_file_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        const string content = """{"Devices":[{"Id":"d","Name":"D","Kind":"Floppy"}]}""";
        File.WriteAllText(StatePath, content);

        Assert.Equal("state_corrupt", Assert.Throws<DomainException>(() => new JsonStateStore(StatePath).Load()).Code);
        Assert.Equal(content, File.ReadAllText(StatePath));
    }
}

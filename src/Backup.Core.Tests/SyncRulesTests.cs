using Xunit;

namespace Backup.Core.Tests;

public class SyncRulesTests
{
    private static StorageObject Obj(string id, string device, string path, bool encrypted) =>
        new(id, device, path, encrypted, encrypted ? "pw" : null);

    private static BackupGroup Group(params StorageObject[] objects) => new() { Id = "g", Name = "G", Objects = [.. objects] };

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Theory]
    [InlineData(false, true, SyncKind.ResticBackup)]
    [InlineData(true, true, SyncKind.ResticCopy)]
    [InlineData(false, false, SyncKind.FileSync)]
    [InlineData(true, false, SyncKind.RestoreToDirectory)]
    public void Kind_follows_the_encryption_of_both_sides(bool sourceEncrypted, bool targetEncrypted, SyncKind expected)
    {
        Assert.Equal(expected, SyncRules.KindFor(Obj("a", "d1", "/a", sourceEncrypted), Obj("b", "d2", "/b", targetEncrypted)));
    }

    [Fact]
    public void All_other_objects_are_targets_by_default()
    {
        BackupGroup group = Group(Obj("a", "local", "/a", false), Obj("b", "disk", "/b", false), Obj("c", "gdrive", "/c", true), Obj("d", "gdrive", "/d", true));
        SyncSelection selection = SyncRules.Select(group, "a", null);
        Assert.Equal("a", selection.Source.Id);
        Assert.Equal(["b", "c", "d"], selection.Targets.Select(t => t.Id));
    }

    [Fact]
    public void Some_targets_can_be_chosen_and_repeats_count_once()
    {
        BackupGroup group = Group(Obj("a", "local", "/a", false), Obj("b", "disk", "/b", false), Obj("c", "gdrive", "/c", true));
        SyncSelection selection = SyncRules.Select(group, "a", ["c", "c"]);
        Assert.Equal(["c"], selection.Targets.Select(t => t.Id));
    }

    [Fact]
    public void A_source_is_needed_and_must_be_in_the_group()
    {
        BackupGroup group = Group(Obj("a", "local", "/a", false), Obj("b", "disk", "/b", false));
        Assert.Equal("sync_source_required", CodeOf(() => SyncRules.Select(group, null, null)));
        Assert.Equal("sync_source_required", CodeOf(() => SyncRules.Select(group, "zzz", null)));
    }

    [Fact]
    public void The_source_cannot_be_its_own_target_and_targets_must_exist()
    {
        BackupGroup group = Group(Obj("a", "local", "/a", false), Obj("b", "disk", "/b", false));
        Assert.Equal("sync_target_is_source", CodeOf(() => SyncRules.Select(group, "a", ["a", "b"])));
        Assert.Equal("object_not_found", CodeOf(() => SyncRules.Select(group, "a", ["zzz"])));
    }

    [Fact]
    public void At_least_one_target_is_needed()
    {
        Assert.Equal("sync_targets_required", CodeOf(() => SyncRules.Select(Group(Obj("a", "local", "/a", false)), "a", null)));
        BackupGroup group = Group(Obj("a", "local", "/a", false), Obj("b", "disk", "/b", false));
        Assert.Equal("sync_targets_required", CodeOf(() => SyncRules.Select(group, "a", [])));
    }

    [Theory]
    [InlineData("/data", "/data/backup")]
    [InlineData("/data/backup", "/data")]
    [InlineData("/", "/data")]
    public void Objects_inside_each_other_on_one_device_are_refused(string sourcePath, string targetPath)
    {
        BackupGroup group = Group(Obj("a", "disk", sourcePath, false), Obj("b", "disk", targetPath, true));
        Assert.Equal("sync_overlap", CodeOf(() => SyncRules.Select(group, "a", null)));
    }

    [Fact]
    public void Similar_names_and_other_devices_are_not_an_overlap()
    {
        BackupGroup group = Group(Obj("a", "disk", "/data", false), Obj("b", "disk", "/data2", true), Obj("c", "local", "/data/x", true));
        Assert.Equal(2, SyncRules.Select(group, "a", null).Targets.Count);
    }
}

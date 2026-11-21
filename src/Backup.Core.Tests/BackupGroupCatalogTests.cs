using Xunit;

namespace Backup.Core.Tests;

public class BackupGroupCatalogTests
{
    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void AddGroup_makes_an_empty_group_from_a_name_only()
    {
        BackupGroupCatalog catalog = new();
        BackupGroup group = catalog.AddGroup("  Photos ");
        Assert.Equal("Photos", group.Name);
        Assert.Empty(group.Objects);
        Assert.Same(group, catalog.Find(group.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AddGroup_needs_a_name(string? name)
    {
        Assert.Equal("group_name_invalid", CodeOf(() => new BackupGroupCatalog().AddGroup(name)));
    }

    [Fact]
    public void Group_names_are_unique_ignoring_case()
    {
        BackupGroupCatalog catalog = new();
        BackupGroup a = catalog.AddGroup("Media");
        catalog.AddGroup("Docs");
        Assert.Equal("group_name_taken", CodeOf(() => catalog.AddGroup("media")));
        Assert.Equal("group_name_taken", CodeOf(() => catalog.RenameGroup(a.Id, "DOCS")));
        Assert.Equal("MEDIA", catalog.RenameGroup(a.Id, "MEDIA").Name);
    }

    [Fact]
    public void Trailing_slash_is_the_same_path()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject item = catalog.AddObject(group, "media", "/path/to/some/", false, null);
        Assert.Equal("/path/to/some", item.Path);
        Assert.Equal("object_exists", CodeOf(() => catalog.AddObject(group, "media", "/path/to/some", true, "pw")));
    }

    [Fact]
    public void One_device_may_hold_several_objects_with_their_own_encryption()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject a = catalog.AddObject(group, "media", "/path/a", true, "secret");
        StorageObject b = catalog.AddObject(group, "media", "/path/b", false, "ignored");
        Assert.True(a.Encrypted);
        Assert.Equal("secret", a.Password);
        Assert.False(b.Encrypted);
        Assert.Null(b.Password);
        Assert.Equal(2, catalog.Get(group).Objects.Count);
    }

    [Fact]
    public void Same_path_on_other_devices_is_allowed()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        catalog.AddObject(group, "local", "/data", false, null);
        catalog.AddObject(group, "gdrive", "/data", true, "pw");
        Assert.Equal(2, catalog.Get(group).Objects.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Encrypted_object_needs_a_password(string? password)
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        Assert.Equal("object_password_required", CodeOf(() => catalog.AddObject(group, "gdrive", "/x", true, password)));
        Assert.Empty(catalog.Get(group).Objects);
    }

    [Fact]
    public void Object_needs_a_device_and_a_valid_path()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        Assert.Equal("object_device_required", CodeOf(() => catalog.AddObject(group, " ", "/x", false, null)));
        Assert.Equal("path_invalid", CodeOf(() => catalog.AddObject(group, "local", "x", false, null)));
        Assert.Equal("group_not_found", CodeOf(() => catalog.AddObject("nope", "local", "/x", false, null)));
    }

    [Fact]
    public void Update_keeps_the_saved_password_only_if_still_encrypted()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject item = catalog.AddObject(group, "gdrive", "/x", true, "old");

        Assert.Equal("old", catalog.UpdateObject(group, item.Id, "gdrive", "/y", true, "").Password);
        Assert.Equal("new", catalog.UpdateObject(group, item.Id, "gdrive", "/y", true, "new").Password);

        Assert.Null(catalog.UpdateObject(group, item.Id, "gdrive", "/y", false, null).Password);
        Assert.Equal("object_password_required", CodeOf(() => catalog.UpdateObject(group, item.Id, "gdrive", "/y", true, "")));
    }

    [Fact]
    public void Update_cannot_collide_with_another_object_but_may_keep_its_own_location()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject a = catalog.AddObject(group, "local", "/a", false, null);
        catalog.AddObject(group, "local", "/b", false, null);
        catalog.UpdateObject(group, a.Id, "local", "/a/", true, "pw");
        Assert.Equal("object_exists", CodeOf(() => catalog.UpdateObject(group, a.Id, "local", "/b/", false, null)));
        Assert.Equal("object_not_found", CodeOf(() => catalog.UpdateObject(group, "nope", "local", "/c", false, null)));
    }

    [Fact]
    public void Retention_belongs_to_an_encrypted_object_and_survives_an_update()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject repo = catalog.AddObject(group, "gdrive", "/r", true, "pw");
        StorageObject plain = catalog.AddObject(group, "local", "/p", false, null);

        Assert.Equal(new RetentionPolicy(KeepLast: 5), catalog.SetRetention(group, repo.Id, new RetentionPolicy(KeepLast: 5)).Retention);
        Assert.Equal("retention_needs_repository", CodeOf(() => catalog.SetRetention(group, plain.Id, new RetentionPolicy(KeepLast: 1))));
        Assert.Equal("retention_empty", CodeOf(() => catalog.SetRetention(group, repo.Id, new RetentionPolicy())));
        Assert.Equal("object_not_found", CodeOf(() => catalog.SetRetention(group, "nope", null)));

        Assert.Equal(5, catalog.UpdateObject(group, repo.Id, "gdrive", "/r2", true, "").Retention!.KeepLast);
        Assert.Null(catalog.SetRetention(group, repo.Id, null).Retention);
    }

    [Fact]
    public void Retention_is_dropped_when_an_object_stops_being_encrypted()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        StorageObject repo = catalog.AddObject(group, "local", "/r", true, "pw");
        catalog.SetRetention(group, repo.Id, new RetentionPolicy(KeepLast: 2));
        Assert.Null(catalog.UpdateObject(group, repo.Id, "local", "/r", false, null).Retention);
    }

    [Fact]
    public void Removing_forgets_configuration_only()
    {
        BackupGroupCatalog catalog = new();
        BackupGroup group = catalog.AddGroup("G");
        StorageObject item = catalog.AddObject(group.Id, "local", "/a", false, null);
        catalog.RemoveObject(group.Id, item.Id);
        Assert.Empty(group.Objects);
        Assert.Equal("object_not_found", CodeOf(() => catalog.RemoveObject(group.Id, item.Id)));
        catalog.RemoveGroup(group.Id);
        Assert.Empty(catalog.Groups);
        Assert.Equal("group_not_found", CodeOf(() => catalog.RemoveGroup(group.Id)));
    }

    [Theory]
    [InlineData("pass\0word")]
    [InlineData("\0")]
    public void A_password_with_a_null_character_is_refused_when_it_is_saved_and_not_when_a_run_starts(string password)
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;

        Assert.Equal("object_password_invalid", CodeOf(() => catalog.AddObject(group, "local", "/x", true, password)));
        Assert.Empty(catalog.Get(group).Objects);

        StorageObject item = catalog.AddObject(group, "local", "/x", true, "fine");
        Assert.Equal("object_password_invalid", CodeOf(() => catalog.UpdateObject(group, item.Id, "local", "/x", true, password)));
        Assert.Equal("fine", catalog.Get(group).Objects.Single().Password);
    }

    [Fact]
    public void A_long_password_is_fine_up_to_the_limit_and_a_longer_one_is_refused()
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        catalog.AddObject(group, "local", "/a", true, new string('p', BackupGroupCatalog.MaxPasswordLength));
        Assert.Equal("object_password_invalid", CodeOf(() => catalog.AddObject(group, "local", "/b", true, new string('p', BackupGroupCatalog.MaxPasswordLength + 1))));
    }

    [Theory]
    [InlineData("pass word  with spaces ")]
    [InlineData("ünï©ödé-密码-🔑")]
    [InlineData("quotes \"'` $HOME ; | & <>")]
    [InlineData("line\nbreak")]
    public void Any_other_text_is_a_password_and_is_kept_exactly_as_it_was_typed(string password)
    {
        BackupGroupCatalog catalog = new();
        string group = catalog.AddGroup("G").Id;
        Assert.Equal(password, catalog.AddObject(group, "local", "/x", true, password).Password);
    }
}

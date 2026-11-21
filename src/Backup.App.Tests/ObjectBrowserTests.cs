using Backup.App.Browsing;
using Backup.Core;
using Backup.Infra.Disks;
using Backup.Infra.Storage;
using Xunit;

namespace Backup.App.Tests;

public sealed class ObjectBrowserTests : IDisposable
{
    private readonly PlannerFixture _fx = new();
    private readonly ObjectBrowser _browser;

    public ObjectBrowserTests() => _browser = new ObjectBrowser(_fx.Workspace, new DeviceResolver(_fx.Probe));

    public void Dispose() => _fx.Dispose();

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void A_folder_is_listed_with_directories_first_and_paths_inside_the_object()
    {
        StorageObject item = _fx.LocalSource();
        File.WriteAllText(Path.Combine(item.Path, "B.txt"), "12345");

        FileListing top = _browser.List(_fx.GroupId, item.Id, null);

        Assert.Equal(["sub", "a.txt", "B.txt"], top.Entries.Select(e => e.Name));
        Assert.Equal(["dir", "file", "file"], top.Entries.Select(e => e.Type));
        Assert.Equal(["/sub", "/a.txt", "/B.txt"], top.Entries.Select(e => e.Path));
        Assert.Equal([null, 5L, 5L], top.Entries.Select(e => e.Size));
        Assert.False(top.Truncated);

        FileListing sub = _browser.List(_fx.GroupId, item.Id, "/sub/");
        Assert.Equal(["/sub/b.txt"], sub.Entries.Select(e => e.Path));
    }

    [Fact]
    public void An_object_on_a_disk_is_listed_below_its_mount_point_and_a_missing_disk_is_refused()
    {
        Directory.CreateDirectory(Path.Combine(_fx.Mount, "media/photos"));
        File.WriteAllText(Path.Combine(_fx.Mount, "media/clip.mp4"), "x");
        StorageObject item = _fx.Add("disk", "/media", false);

        Assert.Equal(["photos", "clip.mp4"], _browser.List(_fx.GroupId, item.Id, "/").Entries.Select(e => e.Name));

        _fx.Probe.Status = new DiskStatus(DiskState.NotConnected);
        Assert.Equal("disk_not_connected", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/")));
    }

    [Fact]
    public void Links_are_shown_as_links_and_are_never_followed()
    {
        StorageObject item = _fx.LocalSource();
        string outside = Directory.CreateDirectory(Path.Combine(_fx.Root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "s");
        File.CreateSymbolicLink(Path.Combine(item.Path, "escape"), outside);

        FileListing top = _browser.List(_fx.GroupId, item.Id, "/");
        Assert.Equal("symlink", top.Entries.Single(e => e.Name == "escape").Type);
        Assert.Equal("path_escapes_object", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/escape")));
        Assert.Equal("path_escapes_object", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/escape/")));
    }

    [Fact]
    public void Bad_paths_and_wrong_objects_are_refused()
    {
        StorageObject item = _fx.LocalSource();
        StorageObject repo = _fx.Add("local", Path.Combine(_fx.Root, "repo"), true);

        Assert.Equal("path_invalid", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "relative")));
        Assert.Equal("path_invalid", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/sub/../..")));
        Assert.Equal("path_not_found", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/nothing")));
        Assert.Equal("path_not_found", CodeOf(() => _browser.List(_fx.GroupId, item.Id, "/a.txt")));
        Assert.Equal("object_is_a_repository", CodeOf(() => _browser.List(_fx.GroupId, repo.Id, "/")));
        Assert.Equal("object_not_found", CodeOf(() => _browser.List(_fx.GroupId, "nope", "/")));
        Assert.Equal("group_not_found", CodeOf(() => _browser.List("nope", item.Id, "/")));
    }

    [Fact]
    public void A_very_large_folder_is_cut_and_says_so()
    {
        string dir = Directory.CreateDirectory(Path.Combine(_fx.Home, "many")).FullName;
        for (int i = 0; i < ObjectBrowser.MaxEntries + 5; i++)
        {
            File.WriteAllText(Path.Combine(dir, $"f{i:D5}"), "");
        }

        StorageObject item = _fx.Add("local", dir, false);
        FileListing listing = _browser.List(_fx.GroupId, item.Id, "/");
        Assert.Equal(ObjectBrowser.MaxEntries, listing.Entries.Count);
        Assert.True(listing.Truncated);
    }
}

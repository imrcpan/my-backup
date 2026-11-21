using Backup.Infra.Storage;
using Xunit;

namespace Backup.Infra.Tests;

[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class DirectoryScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("scanner").FullName;

    public void Dispose()
    {
        string closed = Path.Combine(_root, "closed");
        if (Directory.Exists(closed))
        {
            File.SetUnixFileMode(closed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_root, true);
    }

    private void Put(string relative, int size)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public void Files_and_bytes_are_counted_below_the_directory_and_the_first_ones_are_listed()
    {
        Put("a.txt", 10);
        Put("sub/b.txt", 20);
        Put("sub/deeper/c.txt", 30);

        DirectoryScan scan = DirectoryScanner.Scan(_root, 2, CancellationToken.None);

        Assert.Equal(3, scan.Files);
        Assert.Equal(60, scan.Bytes);
        Assert.Equal(2, scan.First.Count);
        Assert.True(scan.Truncated);
        Assert.All(scan.First, f => Assert.StartsWith(_root, f.Path));
    }

    [Fact]
    public void An_empty_directory_and_a_directory_of_only_folders_have_no_files()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty/inner"));
        DirectoryScan scan = DirectoryScanner.Scan(_root, 10, CancellationToken.None);
        Assert.Equal(0, scan.Files);
        Assert.False(scan.Truncated);
    }

    [Fact]
    public void A_folder_that_cannot_be_read_is_skipped_and_does_not_stop_the_count()
    {
        Put("open/a.txt", 5);
        Put("closed/secret.txt", 7);
        File.SetUnixFileMode(Path.Combine(_root, "closed"), UnixFileMode.None);

        DirectoryScan scan = DirectoryScanner.Scan(_root, 10, CancellationToken.None);

        // Running as root reads everything; otherwise the closed folder is skipped. Either way the count is the readable part.
        Assert.InRange(scan.Files, 1, 2);
        Assert.Contains(scan.First, f => f.Path.EndsWith("open/a.txt"));
    }

    [Fact]
    public void A_link_that_points_back_up_does_not_make_the_scan_endless_and_links_are_not_counted_twice()
    {
        Put("real/a.txt", 5);
        File.CreateSymbolicLink(Path.Combine(_root, "real/loop"), _root);
        File.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));

        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(20));
        DirectoryScan scan = DirectoryScanner.Scan(_root, 10, limit.Token);

        Assert.Equal(1, scan.Files);
        Assert.Equal(5, scan.Bytes);
    }

    [Fact]
    public void A_scan_that_is_cancelled_stops()
    {
        for (int i = 0; i < 300; i++)
        {
            Put($"d{i % 10}/f{i}", 1);
        }

        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => DirectoryScanner.Scan(_root, 10, cancel.Token));
    }
}

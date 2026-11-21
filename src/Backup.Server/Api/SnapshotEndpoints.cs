using Backup.App.Browsing;
using Backup.Infra.Restic;

namespace Backup.Server.Api;

public sealed record SnapshotDto(string Id, string ShortId, DateTimeOffset Time, string[] Paths, string Hostname, long? Files, long? Bytes);

public sealed record FileEntryDto(string Name, string Type, string Path, long? Size, DateTimeOffset? Modified);

public sealed record FileListingDto(FileEntryDto[] Entries, bool Truncated);

public static class SnapshotEndpoints
{
    public static SnapshotDto ToDto(ResticSnapshot snapshot) =>
        new(snapshot.Id, snapshot.ShortId, snapshot.Time, snapshot.Paths, snapshot.Hostname, snapshot.Summary?.TotalFilesProcessed, snapshot.Summary?.TotalBytesProcessed);

    public static void Map(WebApplication app)
    {
        // The files of a plain storage object, read from the device.
        app.MapGet("/api/groups/{groupId}/objects/{objectId}/files", (string groupId, string objectId, string? path, ObjectBrowser browser) =>
        {
            FileListing listing = browser.List(groupId, objectId, path);
            return new FileListingDto([.. listing.Entries.Select(e => new FileEntryDto(e.Name, e.Type, e.Path, e.Size, e.Modified))], listing.Truncated);
        });

        RouteGroupBuilder repo = app.MapGroup("/api/groups/{groupId}/objects/{objectId}/snapshots");

        repo.MapGet("/", async (string groupId, string objectId, RepositoryReader reader, CancellationToken cancellationToken) =>
            (await reader.SnapshotsAsync(groupId, objectId, cancellationToken)).Select(ToDto).OrderByDescending(s => s.Time).ToArray());

        repo.MapGet("/{snapshotId}/files", async (string groupId, string objectId, string snapshotId, string? path, RepositoryReader reader, CancellationToken cancellationToken) =>
            (await reader.FilesAsync(groupId, objectId, snapshotId, path, cancellationToken))
                .OrderBy(n => n.Type == "dir" ? 0 : 1).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

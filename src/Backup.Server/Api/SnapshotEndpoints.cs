using Backup.App.Browsing;
using Backup.Infra.Restic;

namespace Backup.Server.Api;

public sealed record SnapshotDto(string Id, string ShortId, DateTimeOffset Time, string[] Paths, string Hostname, long? Files, long? Bytes);

public static class SnapshotEndpoints
{
    public static SnapshotDto ToDto(ResticSnapshot snapshot) =>
        new(snapshot.Id, snapshot.ShortId, snapshot.Time, snapshot.Paths, snapshot.Hostname, snapshot.Summary?.TotalFilesProcessed, snapshot.Summary?.TotalBytesProcessed);

    public static void Map(WebApplication app)
    {
        RouteGroupBuilder repo = app.MapGroup("/api/groups/{groupId}/objects/{objectId}/snapshots");

        repo.MapGet("/", async (string groupId, string objectId, RepositoryReader reader, CancellationToken cancellationToken) =>
            (await reader.SnapshotsAsync(groupId, objectId, cancellationToken)).Select(ToDto).OrderByDescending(s => s.Time).ToArray());

        repo.MapGet("/{snapshotId}/files", async (string groupId, string objectId, string snapshotId, string? path, RepositoryReader reader, CancellationToken cancellationToken) =>
            (await reader.FilesAsync(groupId, objectId, snapshotId, path, cancellationToken))
                .OrderBy(n => n.Type == "dir" ? 0 : 1).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

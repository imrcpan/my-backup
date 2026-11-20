using System.Text.Json.Serialization;

namespace Backup.Infra.Restic;

/// <summary>A restic repository: where it is, and the password that opens it.</summary>
/// <param name="Location">A directory, or "rclone:remote:path" for a cloud drive.</param>
public sealed record ResticRepository(string Location, string Password);

public sealed record ResticSnapshot(
    string Id,
    string ShortId,
    DateTimeOffset Time,
    string Tree,
    string[] Paths,
    string Hostname,
    string[]? Tags,
    ResticSnapshotSummary? Summary)
{
    /// <summary>
    /// A copy of a snapshot into another repository has a new id but the same tree, paths,
    /// time and host; that is how an earlier copy is recognized.
    /// </summary>
    public bool SameContentAs(ResticSnapshot other) =>
        Tree == other.Tree && Time == other.Time && Hostname == other.Hostname && Paths.SequenceEqual(other.Paths);
}

public sealed record ResticSnapshotSummary(
    long FilesNew,
    long FilesChanged,
    long FilesUnmodified,
    long DataAdded,
    long TotalFilesProcessed,
    long TotalBytesProcessed);

/// <param name="Type">"file", "dir", "symlink" and so on.</param>
public sealed record ResticNode(string Name, string Type, string Path, long? Size, DateTimeOffset? Mtime);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ResticSnapshot[]))]
[JsonSerializable(typeof(ResticNode))]
internal sealed partial class ResticJson : JsonSerializerContext;

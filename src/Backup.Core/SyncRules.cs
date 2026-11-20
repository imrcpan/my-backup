namespace Backup.Core;

/// <summary>How the newest copy reaches a target, which depends on whether each side is encrypted.</summary>
public enum SyncKind
{
    /// <summary>Plain source, encrypted target: a new restic snapshot of the source.</summary>
    ResticBackup,

    /// <summary>Encrypted source, encrypted target: the latest snapshot is copied between repositories.</summary>
    ResticCopy,

    /// <summary>Plain source, plain target: the target is made to look like the source.</summary>
    FileSync,

    /// <summary>Encrypted source, plain target: the latest snapshot is restored into an empty directory.</summary>
    RestoreToDirectory,
}

/// <summary>The newest copy and the objects that are brought up to date from it.</summary>
public sealed record SyncSelection(StorageObject Source, IReadOnlyList<StorageObject> Targets);

public static class SyncRules
{
    public static SyncKind KindFor(StorageObject source, StorageObject target) => (source.Encrypted, target.Encrypted) switch
    {
        (false, true) => SyncKind.ResticBackup,
        (true, true) => SyncKind.ResticCopy,
        (false, false) => SyncKind.FileSync,
        (true, false) => SyncKind.RestoreToDirectory,
    };

    /// <summary>
    /// Checks the choice of the user: one object is the newest copy, the others
    /// (all of them if none are named) are the targets.
    /// </summary>
    public static SyncSelection Select(BackupGroup group, string? sourceId, IReadOnlyList<string>? targetIds)
    {
        StorageObject source = group.Objects.Find(o => o.Id == sourceId)
            ?? throw new DomainException("sync_source_required", "Choose the object that holds the newest copy.");

        List<StorageObject> targets = [];
        if (targetIds == null)
        {
            targets.AddRange(group.Objects.Where(o => o.Id != source.Id));
        }
        else
        {
            foreach (string id in targetIds.Distinct())
            {
                if (id == source.Id)
                {
                    throw new DomainException("sync_target_is_source", "The newest copy cannot be a target of itself.");
                }

                targets.Add(group.Objects.Find(o => o.Id == id)
                    ?? throw new DomainException("object_not_found", "The storage object does not exist."));
            }
        }

        if (targets.Count == 0)
        {
            throw new DomainException("sync_targets_required", "Choose at least one target.");
        }

        foreach (StorageObject target in targets)
        {
            if (Overlap(source, target))
            {
                throw new DomainException("sync_overlap", "A target and the newest copy lie inside each other.");
            }
        }

        return new SyncSelection(source, targets);
    }

    private static bool Overlap(StorageObject a, StorageObject b) =>
        a.DeviceId == b.DeviceId && (Inside(a.Path, b.Path) || Inside(b.Path, a.Path));

    private static bool Inside(string path, string parent) =>
        parent == "/" || path == parent || path.StartsWith(parent + "/", StringComparison.Ordinal);
}

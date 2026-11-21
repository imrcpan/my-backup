using System.Text.Json.Serialization;

namespace Backup.Core.Runs;

public enum RunKind
{
    /// <summary>Bring the targets up to date from the newest copy.</summary>
    Sync,

    /// <summary>Restore files of a snapshot into a new directory.</summary>
    Restore,

    /// <summary>Remove snapshots, by the retention policy or by choice.</summary>
    Forget,

    /// <summary>Delete the data that no snapshot uses.</summary>
    Prune,

    /// <summary>Check the integrity of a repository.</summary>
    Check,
}

public enum RunState
{
    /// <summary>Shown to the user, who has not confirmed it. Nothing was changed.</summary>
    Planned,
    Running,
    Succeeded,
    Failed,
    Cancelled,

    /// <summary>The application stopped while it was running.</summary>
    Interrupted,

    /// <summary>A plan that was not confirmed in time or was replaced.</summary>
    Discarded,
}

public enum StepKind
{
    ResticBackup,
    ResticCopy,
    FileSync,
    RestoreToDirectory,
    Restore,
    Forget,
    Prune,
    Check,
}

public enum StepState
{
    Pending,

    /// <summary>Cannot be done as it stands; see <see cref="RunStep.ErrorCode"/>.</summary>
    Blocked,

    /// <summary>The target is current; there is nothing to do.</summary>
    UpToDate,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped,
    Interrupted,
}

/// <param name="Action">"new", "modified", "copy", "delete", "restore", "remove" or "keep".</param>
public sealed class PlanLine
{
    public string Action { get; set; } = "";

    public string Path { get; set; } = "";

    public long Size { get; set; }
}

public enum CheckDepth
{
    Structure,
    Subset,
    Full,
}

/// <summary>What a step needs besides its object. They are fixed when the plan is made, so the run does what was shown.</summary>
public sealed class StepArguments
{
    /// <summary>The snapshot to copy or restore.</summary>
    public string? SnapshotId { get; set; }

    /// <summary>The directory inside the snapshot that becomes the top of a restore, if the snapshot has just one.</summary>
    public string? SnapshotPath { get; set; }

    /// <summary>The new directory a restore writes into.</summary>
    public string? TargetDirectory { get; set; }

    /// <summary>The paths of a snapshot to restore; all of it if empty.</summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>The snapshots to forget, if they were chosen one by one.</summary>
    public List<string> SnapshotIds { get; set; } = [];

    /// <summary>The policy to forget by, if no snapshots were chosen.</summary>
    public RetentionPolicy? Retention { get; set; }

    public CheckDepth CheckDepth { get; set; }

    public int SubsetPercent { get; set; } = 10;

    /// <summary>Whether a prune follows a forget.</summary>
    public bool PruneAfter { get; set; }

    public StepArguments Clone() => new()
    {
        SnapshotId = SnapshotId,
        SnapshotPath = SnapshotPath,
        TargetDirectory = TargetDirectory,
        Paths = [.. Paths],
        SnapshotIds = [.. SnapshotIds],
        Retention = Retention,
        CheckDepth = CheckDepth,
        SubsetPercent = SubsetPercent,
        PruneAfter = PruneAfter,
    };
}

/// <summary>One thing a run does to one storage object.</summary>
public sealed class RunStep
{
    public string Id { get; set; } = "";

    public StepKind Kind { get; set; }

    /// <summary>The object that is changed (or checked, for a check).</summary>
    public string TargetObjectId { get; set; } = "";

    public StepState State { get; set; }

    public StepArguments Arguments { get; set; } = new();

    /// <summary>The diff shown to the user. Cut to a fixed number of lines.</summary>
    public List<PlanLine> Lines { get; set; } = [];

    public bool LinesTruncated { get; set; }

    public long ToAdd { get; set; }

    public long ToChange { get; set; }

    public long ToDelete { get; set; }

    public long Bytes { get; set; }

    /// <summary>The bytes of the files that the step deletes; <see cref="Bytes"/> is what it copies or uploads.</summary>
    public long BytesToDelete { get; set; }

    /// <summary>Whether the whole list of what the step does was kept, to be downloaded; the lines above are only the start of it.</summary>
    public bool HasDiff { get; set; }

    /// <summary>
    /// The whole list, only while the plan is being made: the planner leaves it here and the service that registers the
    /// plan writes it to a file and clears it. It is never saved with the run, copied, or sent to the interface.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<PlanLine>? CompleteDiff { get; set; }

    /// <summary>Stable keys the interface turns into text, such as "creates_repository".</summary>
    public List<string> Notes { get; set; } = [];

    public double Progress { get; set; }

    public long BytesDone { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>A copy that shares nothing with this step, for whoever reads it while the run goes on.</summary>
    public RunStep Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        TargetObjectId = TargetObjectId,
        State = State,
        Arguments = Arguments.Clone(),
        Lines = [.. Lines.Select(l => new PlanLine { Action = l.Action, Path = l.Path, Size = l.Size })],
        LinesTruncated = LinesTruncated,
        ToAdd = ToAdd,
        ToChange = ToChange,
        ToDelete = ToDelete,
        Bytes = Bytes,
        BytesToDelete = BytesToDelete,
        HasDiff = HasDiff,
        Notes = [.. Notes],
        Progress = Progress,
        BytesDone = BytesDone,
        ErrorCode = ErrorCode,
        ErrorMessage = ErrorMessage,
    };

    /// <summary>Whether this step removes data that cannot be had back. A step with nothing to do removes nothing.</summary>
    public bool Destructive => State != StepState.UpToDate && (Kind is StepKind.Forget or StepKind.Prune || ToDelete > 0);
}

public sealed class Run
{
    public string Id { get; set; } = "";

    public RunKind Kind { get; set; }

    public string GroupId { get; set; } = "";

    public string GroupName { get; set; } = "";

    /// <summary>For a sync, the object with the newest copy; empty otherwise.</summary>
    public string SourceObjectId { get; set; } = "";

    /// <summary>What the plan was made from. If the group changed after it, the plan is stale.</summary>
    public string Fingerprint { get; set; } = "";

    public RunState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public List<RunStep> Steps { get; set; } = [];

    public string? ErrorMessage { get; set; }

    public bool Destructive => Steps.Any(s => s.Destructive);

    /// <summary>A copy that shares nothing with this run.</summary>
    public Run Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        GroupId = GroupId,
        GroupName = GroupName,
        SourceObjectId = SourceObjectId,
        Fingerprint = Fingerprint,
        State = State,
        CreatedAt = CreatedAt,
        StartedAt = StartedAt,
        FinishedAt = FinishedAt,
        Steps = [.. Steps.Select(s => s.Clone())],
        ErrorMessage = ErrorMessage,
    };

    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The conditions for starting a plan: it is still a plan, it is recent, nothing in it is blocked,
    /// and a plan that deletes something was acknowledged on purpose.
    /// </summary>
    /// <exception cref="DomainException">"run_not_planned", "run_expired", "run_blocked", "run_acknowledge_required".</exception>
    public void EnsureCanStart(DateTimeOffset now, bool acknowledgedDestructive)
    {
        if (State != RunState.Planned)
        {
            throw new DomainException("run_not_planned", "Only a plan that is waiting can be started.");
        }

        if (now - CreatedAt > PlanLifetime)
        {
            throw new DomainException("run_expired", "The plan is too old. Make a new one.");
        }

        if (Steps.Count == 0 || Steps.Any(s => s.State == StepState.Blocked))
        {
            throw new DomainException("run_blocked", "The plan has steps that cannot be done.");
        }

        if (Destructive && !acknowledgedDestructive)
        {
            throw new DomainException("run_acknowledge_required", "This run deletes data. Confirm it explicitly.");
        }
    }

    public bool IsFinished => State is RunState.Succeeded or RunState.Failed or RunState.Cancelled or RunState.Interrupted or RunState.Discarded;
}

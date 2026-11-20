using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra;
using Backup.Infra.Rclone;
using Backup.Infra.Restic;
using Backup.Infra.Storage;

namespace Backup.App.Planning;

/// <summary>
/// Makes the plans that the user confirms before anything is changed. A plan is made by reading
/// and by dry runs only; a step that cannot be done is marked blocked with the reason.
/// </summary>
public sealed partial class RunPlanner(
    Workspace workspace,
    IDeviceResolver resolver,
    IResticClient restic,
    IRcloneClient rclone,
    TimeProvider time)
{
    public const int MaxLines = 500;

    /// <summary>The group and the devices as they are now, copied so that the plan does not depend on later changes.</summary>
    private sealed record Snapshot(BackupGroup Group, IReadOnlyDictionary<string, StorageDevice> Devices);

    private Snapshot Take(string groupId) => workspace.Read((devices, groups) =>
    {
        BackupGroup group = groups.Get(groupId);
        return new Snapshot(
            new BackupGroup { Id = group.Id, Name = group.Name, Objects = [.. group.Objects] },
            devices.Devices.ToDictionary(d => d.Id));
    });

    private static StorageObject FindObject(Snapshot snapshot, string? objectId) =>
        snapshot.Group.Objects.Find(o => o.Id == objectId)
            ?? throw new DomainException("object_not_found", "The storage object does not exist.");

    private ResolvedLocation Resolve(Snapshot snapshot, StorageObject item, bool forWrite)
    {
        StorageDevice device = snapshot.Devices.GetValueOrDefault(item.DeviceId)
            ?? throw new DomainException("device_not_found", "The storage device does not exist.");
        return resolver.Resolve(item, device, forWrite);
    }

    private async Task EnsureReadyAsync(ResolvedLocation location, CancellationToken cancellationToken)
    {
        if (location.RcloneRemote == null)
        {
            return;
        }

        RemoteStatus status = await rclone.CheckRemoteAsync(location.RcloneRemote, cancellationToken);
        if (status.State != RemoteState.Ready)
        {
            throw new DomainException(
                status.State == RemoteState.NotConfigured ? "remote_not_configured" : "remote_unreachable",
                status.Message ?? $"The remote '{location.RcloneRemote}' is not ready.");
        }
    }

    private static ResticRepository Repository(ResolvedLocation location) => new(location.ResticLocation, location.Object.Password!);

    private Run NewRun(RunKind kind, Snapshot snapshot, string sourceObjectId, IEnumerable<StorageObject> involved) => new()
    {
        Id = "r" + Guid.NewGuid().ToString("N")[..12],
        Kind = kind,
        GroupId = snapshot.Group.Id,
        GroupName = snapshot.Group.Name,
        SourceObjectId = sourceObjectId,
        Fingerprint = Fingerprint.Of(involved, snapshot.Devices),
        State = RunState.Planned,
        CreatedAt = time.GetUtcNow(),
    };

    private static RunStep NewStep(StepKind kind, string targetObjectId) => new()
    {
        Id = "s" + Guid.NewGuid().ToString("N")[..8],
        Kind = kind,
        TargetObjectId = targetObjectId,
        State = StepState.Pending,
    };

    /// <summary>Runs the planning of one step; a refusal or a tool error does not stop the plan, it blocks that step.</summary>
    private static async Task<RunStep> GuardAsync(StepKind kind, string targetObjectId, Func<RunStep, Task> plan)
    {
        RunStep step = NewStep(kind, targetObjectId);
        try
        {
            await plan(step);
        }
        catch (DomainException error)
        {
            Block(step, error.Code, error.Message);
        }
        catch (ExternalToolException error)
        {
            Block(step, error.Code, error.Message);
        }

        return step;
    }

    private static void Block(RunStep step, string code, string message)
    {
        step.State = StepState.Blocked;
        step.ErrorCode = code;
        step.ErrorMessage = message;
    }

    private static void AddLines(RunStep step, IEnumerable<PlanLine> lines, bool truncated)
    {
        step.Lines.AddRange(lines);
        step.LinesTruncated = truncated;
    }

    private static bool Inside(string path, string parent)
    {
        string p = Path.GetFullPath(path).TrimEnd('/') + "/";
        string d = Path.GetFullPath(parent).TrimEnd('/') + "/";
        return p.StartsWith(d, StringComparison.Ordinal);
    }
}

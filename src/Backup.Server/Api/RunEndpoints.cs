using Backup.App.Execution;
using Backup.App.Planning;
using Backup.Core;
using Backup.Core.Runs;
using Backup.Infra.Logging;

namespace Backup.Server.Api;

public sealed record SyncRunRequest(string? GroupId, string? SourceObjectId, string[]? TargetObjectIds);

public sealed record RestoreRunRequest(string? GroupId, string? ObjectId, string? SnapshotId, string? TargetDirectory, string[]? Paths);

public sealed record ForgetRunRequest(string? GroupId, string? ObjectId, string[]? SnapshotIds, bool Prune);

public sealed record ObjectRunRequest(string? GroupId, string? ObjectId);

public sealed record CheckRunRequest(string? GroupId, string? ObjectId, CheckDepth Depth, int? SubsetPercent);

public sealed record StartRequest(bool AcknowledgeDestructive);

public sealed record RunLogDto(string[] Lines);

public static class RunEndpoints
{
    public static void Map(WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/runs");

        // Planning only reads and tries; the plan is kept and waits for the user to start it.
        api.MapPost("/sync", async (SyncRunRequest request, RunPlanner planner, RunService service, CancellationToken cancellationToken) =>
            service.Register(await planner.PlanSyncAsync(request.GroupId ?? "", request.SourceObjectId, request.TargetObjectIds, cancellationToken)));

        api.MapPost("/restore", async (RestoreRunRequest request, RunPlanner planner, RunService service, CancellationToken cancellationToken) =>
            service.Register(await planner.PlanRestoreAsync(request.GroupId ?? "", request.ObjectId ?? "", request.SnapshotId, request.TargetDirectory, request.Paths, cancellationToken)));

        api.MapPost("/forget", async (ForgetRunRequest request, RunPlanner planner, RunService service, CancellationToken cancellationToken) =>
            service.Register(await planner.PlanForgetAsync(request.GroupId ?? "", request.ObjectId ?? "", request.SnapshotIds, request.Prune, cancellationToken)));

        api.MapPost("/prune", async (ObjectRunRequest request, RunPlanner planner, RunService service, CancellationToken cancellationToken) =>
            service.Register(await planner.PlanPruneAsync(request.GroupId ?? "", request.ObjectId ?? "", cancellationToken)));

        api.MapPost("/check", async (CheckRunRequest request, RunPlanner planner, RunService service, CancellationToken cancellationToken) =>
            service.Register(await planner.PlanCheckAsync(request.GroupId ?? "", request.ObjectId ?? "", request.Depth, request.SubsetPercent ?? 10, cancellationToken)));

        api.MapGet("/", (RunCatalog runs) => runs.List());

        api.MapGet("/{id}", (string id, RunCatalog runs) =>
            runs.Find(id) ?? throw new DomainException("run_not_found", "The run does not exist."));

        api.MapGet("/{id}/log", (string id, int? lines, RunCatalog runs, IRunLog log) =>
        {
            if (runs.Find(id) == null)
            {
                throw new DomainException("run_not_found", "The run does not exist.");
            }

            return new RunLogDto([.. log.Tail(id, Math.Clamp(lines ?? 500, 1, 5000))]);
        });

        api.MapPost("/{id}/start", (string id, StartRequest request, RunService service) => service.Start(id, request.AcknowledgeDestructive));

        api.MapPost("/{id}/cancel", (string id, RunService service, RunCatalog runs) =>
        {
            service.Cancel(id);
            return runs.Find(id);
        });

        api.MapPost("/{id}/discard", (string id, RunService service, RunCatalog runs) =>
        {
            service.Discard(id);
            return runs.Find(id);
        });
    }
}

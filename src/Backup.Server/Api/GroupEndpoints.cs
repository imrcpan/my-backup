using Backup.Core;

namespace Backup.Server.Api;

/// <summary>The password is never sent out; <paramref name="HasPassword"/> tells that there is one.</summary>
public sealed record ObjectDto(string Id, string DeviceId, string Path, bool Encrypted, bool HasPassword, RetentionPolicy? Retention);

public sealed record GroupDto(string Id, string Name, ObjectDto[] Objects);

public sealed record NameRequest(string? Name);

/// <param name="Password">Needed when the object is encrypted. Empty keeps the saved one on an update.</param>
public sealed record ObjectRequest(string? DeviceId, string? Path, bool Encrypted, string? Password);

public sealed record RetentionRequest(RetentionPolicy? Policy);

public static class GroupEndpoints
{
    public static ObjectDto ToDto(StorageObject item) =>
        new(item.Id, item.DeviceId, item.Path, item.Encrypted, !string.IsNullOrEmpty(item.Password), item.Retention);

    public static GroupDto ToDto(BackupGroup group) => new(group.Id, group.Name, [.. group.Objects.Select(ToDto)]);

    public static void Map(WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/groups");

        api.MapGet("/", (Workspace workspace) => workspace.Read((_, groups) => groups.Groups.Select(ToDto).ToArray()));

        api.MapPost("/", (NameRequest request, Workspace workspace) => workspace.Write((_, groups) => ToDto(groups.AddGroup(request.Name))));

        api.MapPut("/{id}", (string id, NameRequest request, Workspace workspace) =>
            workspace.Write((_, groups) => ToDto(groups.RenameGroup(id, request.Name))));

        // Forgets the configuration only. Nothing on any device is deleted.
        api.MapDelete("/{id}", (string id, Workspace workspace) =>
        {
            workspace.Write((_, groups) =>
            {
                groups.RemoveGroup(id);
                return 0;
            });
            return Results.NoContent();
        });

        api.MapPost("/{groupId}/objects", (string groupId, ObjectRequest request, Workspace workspace) =>
            ToDto(workspace.AddObject(groupId, request.DeviceId, request.Path, request.Encrypted, request.Password)));

        api.MapPut("/{groupId}/objects/{objectId}", (string groupId, string objectId, ObjectRequest request, Workspace workspace) =>
            ToDto(workspace.UpdateObject(groupId, objectId, request.DeviceId, request.Path, request.Encrypted, request.Password)));

        api.MapDelete("/{groupId}/objects/{objectId}", (string groupId, string objectId, Workspace workspace) =>
        {
            workspace.Write((_, groups) =>
            {
                groups.RemoveObject(groupId, objectId);
                return 0;
            });
            return Results.NoContent();
        });

        api.MapPut("/{groupId}/objects/{objectId}/retention", (string groupId, string objectId, RetentionRequest request, Workspace workspace) =>
            ToDto(workspace.Write((_, groups) => groups.SetRetention(groupId, objectId, request.Policy))));
    }
}

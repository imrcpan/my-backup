using System.Net;
using System.Text.Json;
using Backup.Infra.Rclone;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backup.Server.Tests;

public sealed class UnexpectedErrorTests : IDisposable
{
    /// <summary>An rclone that breaks in a way nobody planned for, and says a saved password while doing so.</summary>
    private sealed class BrokenRclone : IRcloneClient
    {
        public Task CreateGoogleDriveRemoteAsync(string remote, Action<string>? onLine = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the disk melted, password was hunter2-secret");

        public Task<IReadOnlyList<string>> ListRemotesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RemoteStatus> CheckRemoteAsync(string remote, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RcloneEntry>> ListDirectoriesAsync(string remote, string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RcloneSyncPlan> PlanSyncAsync(string source, string target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SyncAsync(string source, string target, long maxDeletes, Action<RcloneSyncProgress>? onProgress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private readonly ApiFactory _api = new() { Replace = services => services.AddSingleton<IRcloneClient>(new BrokenRclone()) };

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task An_unexpected_error_tells_the_user_what_it_was_and_masks_saved_passwords()
    {
        string group = (await _api.Send("POST", "/api/groups", """{"name":"G"}""")).Body.GetProperty("id").GetString()!;
        await _api.Send("POST", $"/api/groups/{group}/objects", """{"deviceId":"local","path":"/r","encrypted":true,"password":"hunter2-secret"}""");

        (HttpStatusCode status, JsonElement body) = await _api.Send("POST", "/api/devices/gdrive/authorize");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("unexpected", body.GetProperty("code").GetString());
        string message = body.GetProperty("message").GetString()!;
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("the disk melted", message);
        Assert.DoesNotContain("hunter2-secret", message);
    }
}

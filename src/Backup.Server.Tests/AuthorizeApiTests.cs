using System.Net;
using System.Text.Json;
using Backup.Infra.Rclone;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backup.Server.Tests;

public sealed class AuthorizeApiTests : IDisposable
{
    /// <summary>Signing in needs a person and a browser, so rclone is replaced by one that only notes what it was asked.</summary>
    private sealed class SigningRclone : IRcloneClient
    {
        public List<string> Created { get; } = [];

        public Task CreateGoogleDriveRemoteAsync(string remote, Action<string>? onLine = null, CancellationToken cancellationToken = default)
        {
            Created.Add(remote);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListRemotesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RemoteStatus> CheckRemoteAsync(string remote, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RcloneEntry>> ListDirectoriesAsync(string remote, string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RcloneSyncPlan> PlanSyncAsync(string source, string target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SyncAsync(string source, string target, long maxDeletes, Action<RcloneSyncProgress>? onProgress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private readonly SigningRclone _rclone = new();
    private readonly ApiFactory _api;

    public AuthorizeApiTests() => _api = new ApiFactory { Replace = services => services.AddSingleton<IRcloneClient>(_rclone) };

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_cloud_drive_is_signed_in_through_its_remote()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _api.Send("POST", "/api/devices/gdrive/authorize")).Status);
        await _api.Send("POST", "/api/devices", """{"id":"second","name":"Second drive","kind":"Rclone","rcloneRemote":"second-remote"}""");
        Assert.Equal(HttpStatusCode.NoContent, (await _api.Send("POST", "/api/devices/second/authorize")).Status);
        Assert.Equal(["gdrive", "second-remote"], _rclone.Created);
    }

    [Fact]
    public async Task Only_a_cloud_drive_can_be_signed_in_to_and_it_must_exist()
    {
        (HttpStatusCode local, JsonElement body) = await _api.Send("POST", "/api/devices/local/authorize");
        Assert.Equal(HttpStatusCode.BadRequest, local);
        Assert.Equal("device_not_cloud", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Send("POST", "/api/devices/nope/authorize")).Status);
        Assert.Empty(_rclone.Created);
    }
}

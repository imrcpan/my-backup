using System.Text.Json;
using Xunit;

namespace Backup.Server.Tests;

public sealed class SystemApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_system_tells_that_restic_and_rclone_are_there()
    {
        JsonElement system = (await _api.Send("GET", "/api/system")).Body;
        Assert.True(system.GetProperty("restic").GetProperty("available").GetBoolean());
        Assert.StartsWith("restic", system.GetProperty("restic").GetProperty("version").GetString());
        Assert.True(system.GetProperty("rclone").GetProperty("available").GetBoolean());
        Assert.StartsWith("rclone", system.GetProperty("rclone").GetProperty("version").GetString());
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Backup.Infra.Disks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backup.Server.Tests;

public sealed class FakeDiskProbe : IDiskProbe
{
    public DiskStatus Status { get; set; } = new(DiskState.NotConnected);

    public DiskStatus Check(string uuid) => Status;
}

/// <summary>The server in memory, on a scratch data directory, with rclone's local backend as the cloud drive "gdrive".</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        Root = Directory.CreateTempSubdirectory("api-test").FullName;
        Mount = Directory.CreateDirectory(Path.Combine(Root, "mount")).FullName;
        Cloud = Directory.CreateDirectory(Path.Combine(Root, "cloud")).FullName;
        RcloneConfig = Path.Combine(Root, "rclone.conf");
        File.WriteAllText(RcloneConfig, "[gdrive]\ntype = local\n");
        Probe = new FakeDiskProbe { Status = new DiskStatus(DiskState.Mounted, "/dev/fake1", Mount) };
    }

    public string Root { get; }

    public string Mount { get; }

    public string Cloud { get; }

    public string RcloneConfig { get; }

    public FakeDiskProbe Probe { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataDir", Path.Combine(Root, "data"));
        builder.UseSetting("RcloneConfig", RcloneConfig);
        builder.ConfigureServices(services => services.AddSingleton<IDiskProbe>(Probe));
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> Send(string method, string url, string? json = null)
    {
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(new HttpMethod(method), url);
        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonElement body = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, body);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}

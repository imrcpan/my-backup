using System.Text.Json.Serialization;
using Backup.App.Execution;
using Backup.Core;
using Backup.Server;
using Backup.Server.Api;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(CommandLine.Normalize(args));
if (!CommandLine.TryGetPort(builder.Configuration["Port"], 1580, out int port, out string? portError))
{
    Console.Error.WriteLine(portError);
    return 2;
}

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenLocalhost(port);
    // Every request body of this API is a small JSON document.
    kestrel.Limits.MaxRequestBodySize = 1024 * 1024;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJson.Default);
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddBackupServices();
builder.Services.AddHostedService<StartupCheck>();
// A failure to start is said in a sentence below; the host would add a page of stack to it.
builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);

WebApplication app = builder.Build();
HostGuard.Use(app);
ErrorMapping.Use(app);

app.MapGet("/api/ping", () => new Pong("ok"));
DeviceEndpoints.Map(app);
DiskEndpoints.Map(app);
GroupEndpoints.Map(app);
SnapshotEndpoints.Map(app);
RunEndpoints.Map(app);
SystemEndpoints.Map(app);

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RunService>().ShutdownAsync().GetAwaiter().GetResult());

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

try
{
    app.Run();
}
catch (DomainException error)
{
    // The data directory is used by another instance, or the saved state cannot be read.
    Console.Error.WriteLine(error.Message);
    return 3;
}
catch (IOException error) when (error.Message.Contains("in use", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Port {port} is already in use. Stop what uses it, or choose another port, for example: --port {(port < 65535 ? port + 1 : 1580)}");
    return 4;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"The application cannot start: {error.Message}");
    return 3;
}

return 0;

/// <summary>Lets the tests start the server in memory.</summary>
public partial class Program;
